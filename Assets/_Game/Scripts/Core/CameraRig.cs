using RainbowZoo.Animals;
using UnityEngine;

namespace RainbowZoo.Core
{
    /// <summary>
    /// Fixed viewing angle, auto-zoom-to-fit the currently-placed zoo content, up to the 5x3 grid
    /// ceiling (design doc section 11). Beyond that ceiling, the camera holds its distance and
    /// instead allows bounded panning (Zoo Navigation Bars / World Pan Bars, section 14), clamped
    /// so its view never shows past the outer edge of the farthest-placed habitat in any direction.
    ///
    /// Pitch is a tunable field (pitchDegrees), overriding whatever rotation is set on the
    /// Camera's Transform in the Editor -- Phase 11 UX refinement moved this off the placeholder
    /// manually-set angle from earlier phases so it's tunable like everything else here. No
    /// longer a true constant, though: interaction focus (below) temporarily dollies in and eases
    /// pitch to a shallower interactionFocusPitchDegrees while zoomed in on a habitat, then
    /// reverses on zoom-out -- "fixed angle" now means "fixed except during a focus," not "never
    /// changes." Pan directions are plain world +/-X and +/-Z, which only lines up with screen
    /// left/right/up/down because pitch is pure X-axis tilt with zero yaw at all times -- the
    /// interaction-focus math below leans on that same zero-yaw invariant (see FocusPhase) to
    /// guarantee the focused habitat never drifts off-center, so if yaw ever enters the picture
    /// both Pan's world-axis mapping and the focus centering guarantee would need rework.
    ///
    /// Framing math is a flat-plane approximation (world X/Z bounds mapped directly onto
    /// horizontal/vertical FOV), not an exact tilted-frustum-vs-ground-plane solve -- reasonable
    /// given every value here is meant to be tuned by eye, not computed to be pixel-perfect.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance { get; private set; }

        [Header("Framing (placeholder values -- tune freely, no code changes needed)")]
        [Tooltip("Extra world-space margin kept around the fitted content on every side.")]
        [SerializeField] private float paddingWorldUnits = 0.75f;
        [Tooltip("Closest the camera will dolly in, even for a single habitat.")]
        [SerializeField] private float minDistance = 4f;
        [Tooltip("How quickly the camera eases toward a new distance/look-at target.")]
        [SerializeField] private float easeSpeed = 3f;
        [Tooltip("Downward pitch in degrees from horizontal (0 = looking straight ahead, 90 = straight down) -- the resting angle the camera eases back to whenever it isn't mid-interaction-focus. Steeper than the original ~32 deg placeholder so habitats read more square-on, short of a full overhead view.")]
        [SerializeField] private float pitchDegrees = 58f;

        [Header("Grid Ceiling (design doc: 5 columns x 3 rows)")]
        [SerializeField] private int ceilingColumns = 5;
        [SerializeField] private int ceilingRows = 3;

        [Header("Panning (beyond the ceiling only)")]
        [Tooltip("World units per second a held pan bar moves the view.")]
        [SerializeField] private float panSpeed = 6f;
        [Tooltip("How close to a pan bound counts as 'already there' for hiding that direction's bar.")]
        [SerializeField] private float panEpsilon = 0.05f;

        [Header("Interaction Focus (gentle zoom toward a habitat on Pet/Feed/Play)")]
        [Tooltip("Distance to dolly in to when focusing -- never zooms further OUT than the current auto-fit distance, only in, so this only matters once the zoo has grown enough that auto-fit is already farther away than this.")]
        [SerializeField] private float interactionFocusDistance = 7f;
        [Tooltip("Pitch to ease toward while focusing -- lower than the normal pitchDegrees (more level, less top-down) so the animal's animation actually reads instead of being foreshortened by a steep overhead angle. Eases back to pitchDegrees automatically once the focus ends.")]
        [SerializeField] private float interactionFocusPitchDegrees = 15f;
        [Tooltip("How long the focused view holds before easing back to normal auto-fit framing.")]
        [SerializeField] private float interactionFocusHoldSeconds = 3f;
        [Tooltip("Ease speed for the DISTANCE-only leg of zooming in on a habitat -- pitch stays at the resting pitchDegrees throughout this leg (see interactionFocusPitchInSpeed for the angle leg that follows once this one finishes). Sped up per feedback that the zoom-in felt slow; if this field already has a value saved in the Inspector from earlier tuning, bump it there too -- a new code default doesn't overwrite an existing serialized override.")]
        [SerializeField] private float interactionFocusEaseInSpeed = 3.5f;
        [Tooltip("Ease speed for the ANGLE-only leg that follows the distance leg above once zooming in -- kept as its own field so the two legs (distance, then angle) can be tuned independently. The distance leg fully completes first (pure zoom, angle unchanged) before this leg begins, so the habitat never drifts off-center chasing a moving look direction mid-shift.")]
        [SerializeField] private float interactionFocusPitchInSpeed = 3f;
        [Tooltip("Ease speed for BOTH legs of zooming back OUT to normal framing after the hold ends (angle first, then distance -- the reverse order of zooming in). Since these are now two sequential legs instead of one combined motion, the full zoom-out takes roughly twice as long to finish as this single speed value would suggest; split it into separate angle-out/distance-out fields if that reads as too slow once tested.")]
        [SerializeField] private float interactionFocusEaseOutSpeed = 0.8f;

        /// <summary>How close (world units) the eased focus look-at point must get to its target before the distance leg is considered converged.</summary>
        private const float FocusLookAtEpsilon = 0.05f;
        /// <summary>How close (world units) the eased focus distance must get to its target before the distance leg is considered converged.</summary>
        private const float FocusDistanceEpsilon = 0.05f;
        /// <summary>How close (degrees) currentPitch must get to its target before a pitch leg is considered converged.</summary>
        private const float FocusPitchEpsilonDegrees = 0.3f;

        /// <summary>
        /// The interaction-focus sequence, always run in this order on the way in and reversed on
        /// the way out. Splitting distance and angle into separate legs -- never easing both at
        /// once -- is what guarantees the focused habitat stays exactly centered throughout: with
        /// zero yaw, camera position is always exactly "look-at minus forward(pitch)*distance", so
        /// holding two of {look-at, pitch, distance} fixed while only the third eases keeps that
        /// point locked in the center of frame at every intermediate moment, not just once the
        /// motion finishes.
        /// </summary>
        private enum FocusPhase { None, DollyIn, PitchIn, Holding, PitchOut, DollyOut }

        private Camera cam;
        private float ceilingDistance;
        private float currentDistance;
        private Vector3 lookAtTarget;
        private float currentPitch;

        private FocusPhase focusPhase = FocusPhase.None;
        private Vector3 focusLookAtTarget;
        private Vector3 easedFocusLookAt;
        private float focusDistance;
        private float focusHoldRemaining;

        /// <summary>True once content has grown past the 5x3 ceiling and the camera is holding distance -- the only state panning is meaningful in.</summary>
        public bool IsAtCeiling { get; private set; }

        public bool CanPanLeft => IsAtCeiling && CanPanTowards(Vector3.left);
        public bool CanPanRight => IsAtCeiling && CanPanTowards(Vector3.right);
        public bool CanPanForward => IsAtCeiling && CanPanTowards(Vector3.forward);
        public bool CanPanBack => IsAtCeiling && CanPanTowards(Vector3.back);

        private void Awake()
        {
            Instance = this;
            cam = GetComponent<Camera>();

            currentPitch = pitchDegrees;
            transform.rotation = Quaternion.Euler(currentPitch, 0f, 0f);

            // Reproduces the camera's current position exactly (lookAtTarget - forward*distance
            // == transform.position), so Update()'s ease has zero delta to start with -- the
            // placeholder position/angle set before this phase holds as-is until real content
            // exists to frame.
            currentDistance = 10f;
            lookAtTarget = transform.position + transform.forward * currentDistance;
        }

        private void Start()
        {
            if (ZooManager.Instance != null)
            {
                ZooManager.Instance.OnHabitatPlaced += _ => RefreshFraming();
            }

            var ceilingBounds = ComputeBoundsForPlotRange(1, ceilingColumns, 1, ceilingRows);
            ceilingDistance = ComputeFitDistance(new Vector2(ceilingBounds.size.x, ceilingBounds.size.z));

            RefreshFraming();
        }

        private void Update()
        {
            switch (focusPhase)
            {
                case FocusPhase.None:
                    UpdateResting();
                    break;
                case FocusPhase.DollyIn:
                    UpdateFocusDolly(focusLookAtTarget, Mathf.Min(currentDistance, interactionFocusDistance), interactionFocusEaseInSpeed, FocusPhase.PitchIn);
                    break;
                case FocusPhase.PitchIn:
                    UpdateFocusPitch(interactionFocusPitchDegrees, interactionFocusPitchInSpeed, FocusPhase.Holding);
                    break;
                case FocusPhase.Holding:
                    UpdateHolding();
                    break;
                case FocusPhase.PitchOut:
                    UpdateFocusPitch(pitchDegrees, interactionFocusEaseOutSpeed, FocusPhase.DollyOut);
                    break;
                case FocusPhase.DollyOut:
                    UpdateFocusDolly(lookAtTarget, currentDistance, interactionFocusEaseOutSpeed, FocusPhase.None);
                    break;
            }
        }

        /// <summary>Normal auto-fit/pan framing -- pitch is already at rest by the time this state is ever reached (PitchOut always finishes before DollyOut, DollyOut always finishes before handing back here), so plain position easing toward lookAtTarget is safe: forward doesn't change mid-ease in this state.</summary>
        private void UpdateResting()
        {
            currentPitch = pitchDegrees;
            transform.rotation = Quaternion.Euler(currentPitch, 0f, 0f);

            var targetPosition = lookAtTarget - transform.forward * currentDistance;
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * easeSpeed);
        }

        /// <summary>
        /// The distance-only leg (DollyIn / DollyOut): pitch is left exactly as-is (untouched,
        /// not eased) while the look-at point and distance both ease toward their targets.
        /// Position is derived DIRECTLY from (easedFocusLookAt, forward, focusDistance) every
        /// frame rather than through a second layer of position-level easing on top -- with zero
        /// yaw, forward's X component is always 0, so the camera's world-X always exactly equals
        /// easedFocusLookAt.x with no lag, which is what keeps the target from ever drifting
        /// off-center horizontally while this leg is mid-motion.
        /// </summary>
        private void UpdateFocusDolly(Vector3 targetLookAt, float targetDistance, float speed, FocusPhase nextPhase)
        {
            transform.rotation = Quaternion.Euler(currentPitch, 0f, 0f);

            easedFocusLookAt = Vector3.Lerp(easedFocusLookAt, targetLookAt, Time.deltaTime * speed);
            focusDistance = Mathf.Lerp(focusDistance, targetDistance, Time.deltaTime * speed);
            transform.position = easedFocusLookAt - transform.forward * focusDistance;

            if (Vector3.Distance(easedFocusLookAt, targetLookAt) < FocusLookAtEpsilon
                && Mathf.Abs(focusDistance - targetDistance) < FocusDistanceEpsilon)
            {
                easedFocusLookAt = targetLookAt;
                focusDistance = targetDistance;
                transform.position = easedFocusLookAt - transform.forward * focusDistance;
                EnterPhase(nextPhase);
            }
        }

        /// <summary>
        /// The angle-only leg (PitchIn / PitchOut): the look-at point and distance are both held
        /// exactly fixed (already converged by the preceding dolly leg) while only currentPitch
        /// eases. Position is re-derived from the same fixed look-at/distance plus the freshly
        /// eased pitch every frame -- since look-at and distance never move here, and zero yaw
        /// means the camera is always exactly on the "look toward look-at from this distance"
        /// locus for any pitch, the target stays exactly centered as the angle sweeps.
        /// </summary>
        private void UpdateFocusPitch(float targetPitch, float speed, FocusPhase nextPhase)
        {
            currentPitch = Mathf.Lerp(currentPitch, targetPitch, Time.deltaTime * speed);
            transform.rotation = Quaternion.Euler(currentPitch, 0f, 0f);
            transform.position = easedFocusLookAt - transform.forward * focusDistance;

            if (Mathf.Abs(currentPitch - targetPitch) < FocusPitchEpsilonDegrees)
            {
                currentPitch = targetPitch;
                transform.rotation = Quaternion.Euler(currentPitch, 0f, 0f);
                transform.position = easedFocusLookAt - transform.forward * focusDistance;
                EnterPhase(nextPhase);
            }
        }

        /// <summary>Look-at/distance/pitch are already exactly settled from PitchIn's convergence -- nothing to ease, just hold the frame and count the hold timer down.</summary>
        private void UpdateHolding()
        {
            transform.rotation = Quaternion.Euler(currentPitch, 0f, 0f);
            transform.position = easedFocusLookAt - transform.forward * focusDistance;

            focusHoldRemaining -= Time.deltaTime;
            if (focusHoldRemaining <= 0f)
            {
                EnterPhase(FocusPhase.PitchOut);
            }
        }

        private void EnterPhase(FocusPhase phase)
        {
            focusPhase = phase;
            if (phase == FocusPhase.Holding)
            {
                focusHoldRemaining = interactionFocusHoldSeconds;
            }
        }

        /// <summary>
        /// Gentle zoom toward a specific habitat when the player interacts with it (Pet/Feed/Play
        /// tap, see InputRouter.EndGesture), so the resulting reaction reads clearly even once the
        /// zoo has grown large enough that auto-fit framing shows everything at a small size.
        /// Runs distance and angle as two separate sequential legs (see FocusPhase) rather than
        /// blending them, specifically so the habitat never drifts off-center mid-shift: zoom in
        /// (distance) fully completes at the resting angle first, then the angle eases down; zoom
        /// out reverses that order (angle up first, then distance out). Distance never goes
        /// further out than the current auto-fit distance (see the DollyIn call site's Mathf.Min)
        /// -- this is purely a zoom-in aid, never a step backward.
        ///
        /// Re-triggering mid-focus (a new interaction lands while already focused, or while easing
        /// back out from a previous one) just retargets DollyIn from wherever the eased look-at/
        /// distance/pitch currently sit -- no special-casing needed, since every leg always eases
        /// from its own current value regardless of which phase was active when it was called.
        /// </summary>
        public void FocusOnHabitat(Vector3 habitatWorldCenter)
        {
            focusLookAtTarget = habitatWorldCenter;

            if (focusPhase == FocusPhase.None)
            {
                // Starting fresh from resting framing -- seed the eased values from the current
                // resting look-at/distance so DollyIn has no pop to start from.
                easedFocusLookAt = lookAtTarget;
                focusDistance = currentDistance;
            }

            focusPhase = FocusPhase.DollyIn;
        }

        private void RefreshFraming()
        {
            if (ZooManager.Instance == null || ZooManager.Instance.LayoutState.Count == 0) return;

            var bounds = ComputeContentBounds();
            float fitDistance = ComputeFitDistance(new Vector2(bounds.size.x, bounds.size.z));

            if (fitDistance > ceilingDistance)
            {
                // Content has grown past the 5x3 ceiling -- hold the exact framing already in
                // place (doc: "stops zooming out any further and holds at the 5x3-grid zoom
                // level"), rather than continuing to re-center on the growing bounds. Panning
                // is the only thing allowed to move the view from here.
                IsAtCeiling = true;
                currentDistance = ceilingDistance;
                return;
            }

            IsAtCeiling = false;
            lookAtTarget = new Vector3(bounds.center.x, 0f, bounds.center.z);
            currentDistance = Mathf.Max(fitDistance, minDistance);
        }

        /// <summary>
        /// Moves the look-at target by worldDirection*panSpeed*deltaTime, clamped so the camera's
        /// view at the ceiling distance never shows past the outer edge of placed content in any
        /// direction (doc, section 11). No-ops outside IsAtCeiling -- panning isn't meaningful
        /// while auto-zoom is still framing everything on its own.
        /// </summary>
        public void Pan(Vector3 worldDirection, float deltaTime)
        {
            if (!IsAtCeiling || ZooManager.Instance == null) return;

            var (minX, maxX, minZ, maxZ) = ComputePanBounds(ComputeContentBounds());
            var candidate = lookAtTarget + worldDirection.normalized * (panSpeed * deltaTime);
            candidate.x = Mathf.Clamp(candidate.x, minX, maxX);
            candidate.z = Mathf.Clamp(candidate.z, minZ, maxZ);
            lookAtTarget = candidate;
        }

        private bool CanPanTowards(Vector3 worldDirection)
        {
            if (ZooManager.Instance == null) return false;
            var (minX, maxX, minZ, maxZ) = ComputePanBounds(ComputeContentBounds());

            if (worldDirection == Vector3.left) return lookAtTarget.x > minX + panEpsilon;
            if (worldDirection == Vector3.right) return lookAtTarget.x < maxX - panEpsilon;
            if (worldDirection == Vector3.forward) return lookAtTarget.z < maxZ - panEpsilon;
            if (worldDirection == Vector3.back) return lookAtTarget.z > minZ + panEpsilon;
            return false;
        }

        /// <summary>
        /// Range the look-at target may occupy so the view at ceilingDistance never shows past
        /// contentBounds' edge. If the visible window is wider/deeper than the content itself,
        /// that axis collapses to a single centered point (nothing to pan there).
        /// </summary>
        private (float minX, float maxX, float minZ, float maxZ) ComputePanBounds(Bounds contentBounds)
        {
            float halfVFovRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfHFovRad = Mathf.Atan(cam.aspect * Mathf.Tan(halfVFovRad));
            float visibleHalfWidth = ceilingDistance * Mathf.Tan(halfHFovRad);
            float visibleHalfDepth = ceilingDistance * Mathf.Tan(halfVFovRad);

            float minX, maxX, minZ, maxZ;
            if (visibleHalfWidth * 2f >= contentBounds.size.x)
            {
                minX = maxX = contentBounds.center.x;
            }
            else
            {
                minX = contentBounds.min.x + visibleHalfWidth;
                maxX = contentBounds.max.x - visibleHalfWidth;
            }

            if (visibleHalfDepth * 2f >= contentBounds.size.z)
            {
                minZ = maxZ = contentBounds.center.z;
            }
            else
            {
                minZ = contentBounds.min.z + visibleHalfDepth;
                maxZ = contentBounds.max.z - visibleHalfDepth;
            }

            return (minX, maxX, minZ, maxZ);
        }

        private Bounds ComputeContentBounds()
        {
            var placed = ZooManager.Instance.LayoutState.placedAnimals;
            int minCol = int.MaxValue, maxCol = int.MinValue, minRow = int.MaxValue, maxRow = int.MinValue;
            foreach (var entry in placed)
            {
                minCol = Mathf.Min(minCol, entry.plotColumn);
                maxCol = Mathf.Max(maxCol, entry.plotColumn);
                minRow = Mathf.Min(minRow, entry.plotRow);
                maxRow = Mathf.Max(maxRow, entry.plotRow);
            }
            return ComputeBoundsForPlotRange(minCol, maxCol, minRow, maxRow);
        }

        private Bounds ComputeBoundsForPlotRange(int minCol, int maxCol, int minRow, int maxRow)
        {
            var minWorld = ZooManager.Instance.PlotToWorld(new PlotCoordinate(minCol, minRow));
            var maxWorld = ZooManager.Instance.PlotToWorld(new PlotCoordinate(maxCol, maxRow));
            float half = HabitatRuntime.HalfExtent + paddingWorldUnits;

            var bounds = new Bounds();
            bounds.SetMinMax(
                new Vector3(minWorld.x - half, 0f, minWorld.z - half),
                new Vector3(maxWorld.x + half, 0f, maxWorld.z + half));
            return bounds;
        }

        /// <summary>Distance along the camera's forward direction needed so a flat worldSize (X width, Y=world-Z depth) rectangle fits within the camera's FOV in both axes.</summary>
        private float ComputeFitDistance(Vector2 worldSize)
        {
            float halfVFovRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfHFovRad = Mathf.Atan(cam.aspect * Mathf.Tan(halfVFovRad));

            float distanceForDepth = (worldSize.y * 0.5f) / Mathf.Tan(halfVFovRad);
            float distanceForWidth = (worldSize.x * 0.5f) / Mathf.Tan(halfHFovRad);

            return Mathf.Max(distanceForDepth, distanceForWidth, minDistance);
        }
    }
}
