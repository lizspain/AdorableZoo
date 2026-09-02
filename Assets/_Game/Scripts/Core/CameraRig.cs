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
    /// Also auto-attaches a HabitatOcclusionFader to this same GameObject (see Awake) -- a
    /// separate component that reads OcclusionFade01/FocusedHabitatCenter below to fade out any
    /// other habitat sitting in the camera's frustum while one is focused, so closer rows can't
    /// visually block the animal actually being watched.
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
        [SerializeField] private float interactionFocusPitchDegrees = 25f;
        [Tooltip("How long the focused view holds before easing back to normal auto-fit framing.")]
        [SerializeField] private float interactionFocusHoldSeconds = 4f;
        [Tooltip("Total time for the zoom-IN dolly (distance + look-at). The angle change is blended into the tail of this same motion (see interactionFocusPitchTailSeconds) rather than following it as a separate step, so the whole zoom-in reads as one continuous curve.")]
        [SerializeField] private float interactionFocusZoomInDurationSeconds = 1.1f;
        [Tooltip("How much of the END of the zoom-in duration is devoted to blending the angle in (the 'last bit' of the motion) -- e.g. 0.5 of a 1.1s zoom-in means distance eases the whole 1.1s while angle only starts moving at the 0.6s mark, so both finish together. Mirrored at the START of zoom-out (angle leads, since that's when the camera is still close/zoomed and the angle change reads clearly), so the same field governs both directions.")]
        [SerializeField] private float interactionFocusPitchTailSeconds = 0.5f;
        [Tooltip("Total time for the zoom-OUT dolly (distance + look-at) back to normal framing, and angle back to pitchDegrees -- angle leads (see interactionFocusPitchTailSeconds), distance runs the whole duration. Deliberately longer than the zoom-in duration so zooming out still reads as gentle.")]
        [SerializeField] private float interactionFocusZoomOutDurationSeconds = 2f;
        [Tooltip("Alpha the OTHER habitats in frame fade down to while one is focused -- 0.1 = 10% opacity, so they can't visually block the focused animal. See HabitatOcclusionFader.")]
        [Range(0f, 1f)]
        [SerializeField] private float interactionFocusOcclusionOpacity = 0.1f;
        [Tooltip("How much of the END of the zoom-in duration the occlusion fade-out runs over (mirrored at the START of zoom-out for the fade back in) -- kept separate from interactionFocusPitchTailSeconds so the two can be tuned independently even though they default to the same feel.")]
        [SerializeField] private float interactionFocusOcclusionFadeSeconds = 0.5f;

        /// <summary>
        /// The interaction-focus sequence: ZoomIn blends a distance dolly (the whole duration)
        /// with an angle change (only the tail -- see interactionFocusPitchTailSeconds) into one
        /// continuous curve, Holding keeps everything fixed, ZoomOut reverses it (angle leads).
        /// Camera position is derived DIRECTLY from (look-at, pitch, distance) every frame in
        /// every one of these phases -- never through a second layer of position-level easing on
        /// top -- which is what guarantees the focused habitat stays exactly centered throughout:
        /// with zero yaw, "position = look-at minus forward(pitch)*distance" means forward always
        /// points exactly at look-at, for ANY combination of pitch/distance/look-at values, so it
        /// doesn't matter how many of the three are moving at once.
        /// </summary>
        private enum FocusPhase { None, ZoomIn, Holding, ZoomOut }

        private Camera cam;
        private float ceilingDistance;
        private float currentDistance;
        private Vector3 lookAtTarget;

        private FocusPhase focusPhase = FocusPhase.None;
        private Vector3 focusLookAtTarget;
        private float focusHoldRemaining;

        // Authoritative "where the camera conceptually is right now" -- kept accurate every
        // frame in every phase, so FocusOnHabitat can always seed a fresh ZoomIn from wherever
        // the camera actually is (no pop), regardless of what phase was active when it's called.
        private float currentPitch;
        private Vector3 currentLookAt;
        private float currentDistanceValue;

        private float zoomInElapsed;
        private Vector3 zoomInStartLookAt;
        private float zoomInStartDistance;
        private float zoomInStartPitch;

        private float zoomOutElapsed;
        private Vector3 zoomOutStartLookAt;
        private float zoomOutStartDistance;
        private float zoomOutStartPitch;

        /// <summary>0 = normal, 1 = fully faded. Read by HabitatOcclusionFader; non-zero only during the tail of ZoomIn, all of Holding, and the head of ZoomOut.</summary>
        public float OcclusionFade01 { get; private set; }

        /// <summary>World position of the habitat currently being focused -- only meaningful while OcclusionFade01 > 0.</summary>
        public Vector3 FocusedHabitatCenter { get; private set; }

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

            if (GetComponent<HabitatOcclusionFader>() == null)
            {
                gameObject.AddComponent<HabitatOcclusionFader>();
            }

            currentPitch = pitchDegrees;
            transform.rotation = Quaternion.Euler(currentPitch, 0f, 0f);

            // Reproduces the camera's current position exactly (lookAtTarget - forward*distance
            // == transform.position), so Update()'s ease has zero delta to start with -- the
            // placeholder position/angle set before this phase holds as-is until real content
            // exists to frame.
            currentDistance = 10f;
            lookAtTarget = transform.position + transform.forward * currentDistance;
            currentLookAt = lookAtTarget;
            currentDistanceValue = currentDistance;
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
                case FocusPhase.ZoomIn:
                    UpdateZoomIn();
                    break;
                case FocusPhase.Holding:
                    UpdateHolding();
                    break;
                case FocusPhase.ZoomOut:
                    UpdateZoomOut();
                    break;
            }
        }

        /// <summary>Normal auto-fit/pan framing -- pitch is already at rest by the time this state is ever reached, so plain position easing toward lookAtTarget is safe: forward doesn't change mid-ease in this state.</summary>
        private void UpdateResting()
        {
            OcclusionFade01 = 0f;
            currentPitch = pitchDegrees;
            transform.rotation = Quaternion.Euler(currentPitch, 0f, 0f);

            var targetPosition = lookAtTarget - transform.forward * currentDistance;
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * easeSpeed);

            currentLookAt = lookAtTarget;
            currentDistanceValue = currentDistance;
        }

        /// <summary>Distance/look-at ease the whole duration; angle only starts moving in the tail (interactionFocusPitchTailSeconds) so it blends into one curve instead of a hard corner. Occlusion fade-out shares that same tail window.</summary>
        private void UpdateZoomIn()
        {
            zoomInElapsed += Time.deltaTime;
            float duration = Mathf.Max(interactionFocusZoomInDurationSeconds, 0.0001f);
            float t = Mathf.Min(zoomInElapsed, duration);

            float dollyProgress = Mathf.SmoothStep(0f, 1f, t / duration);
            var targetLookAt = focusLookAtTarget;
            float targetDistance = Mathf.Min(currentDistance, interactionFocusDistance);
            var lookAt = Vector3.Lerp(zoomInStartLookAt, targetLookAt, dollyProgress);
            float distance = Mathf.Lerp(zoomInStartDistance, targetDistance, dollyProgress);

            float pitchTail = Mathf.Clamp(interactionFocusPitchTailSeconds, 0.0001f, duration);
            float pitchWindowStart = duration - pitchTail;
            float pitchProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - pitchWindowStart) / pitchTail));
            float pitch = Mathf.Lerp(zoomInStartPitch, interactionFocusPitchDegrees, pitchProgress);

            float fadeWindow = Mathf.Clamp(interactionFocusOcclusionFadeSeconds, 0.0001f, duration);
            float fadeWindowStart = duration - fadeWindow;
            OcclusionFade01 = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - fadeWindowStart) / fadeWindow));
            FocusedHabitatCenter = focusLookAtTarget;

            ApplyDirectFraming(lookAt, pitch, distance);

            if (zoomInElapsed >= duration)
            {
                EnterHolding();
            }
        }

        /// <summary>Look-at/distance/pitch are already exactly settled from ZoomIn's convergence -- nothing to ease, just hold the frame (fully faded) and count the hold timer down.</summary>
        private void UpdateHolding()
        {
            OcclusionFade01 = 1f;
            FocusedHabitatCenter = focusLookAtTarget;
            ApplyDirectFraming(currentLookAt, currentPitch, currentDistanceValue);

            focusHoldRemaining -= Time.deltaTime;
            if (focusHoldRemaining <= 0f)
            {
                EnterZoomOut();
            }
        }

        /// <summary>Reverse of ZoomIn: angle leads (moves in the HEAD of the duration, while still close/zoomed), distance/look-at ease the whole duration back out to whatever the live resting framing currently is. Occlusion fade-back shares the same head window.</summary>
        private void UpdateZoomOut()
        {
            zoomOutElapsed += Time.deltaTime;
            float duration = Mathf.Max(interactionFocusZoomOutDurationSeconds, 0.0001f);
            float t = Mathf.Min(zoomOutElapsed, duration);

            float dollyProgress = Mathf.SmoothStep(0f, 1f, t / duration);
            var lookAt = Vector3.Lerp(zoomOutStartLookAt, lookAtTarget, dollyProgress);
            float distance = Mathf.Lerp(zoomOutStartDistance, currentDistance, dollyProgress);

            float pitchHead = Mathf.Clamp(interactionFocusPitchTailSeconds, 0.0001f, duration);
            float pitchProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / pitchHead));
            float pitch = Mathf.Lerp(zoomOutStartPitch, pitchDegrees, pitchProgress);

            float fadeWindow = Mathf.Clamp(interactionFocusOcclusionFadeSeconds, 0.0001f, duration);
            float fadeProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / fadeWindow));
            OcclusionFade01 = Mathf.Lerp(1f, 0f, fadeProgress);
            FocusedHabitatCenter = focusLookAtTarget;

            ApplyDirectFraming(lookAt, pitch, distance);

            if (zoomOutElapsed >= duration)
            {
                OcclusionFade01 = 0f;
                focusPhase = FocusPhase.None;
            }
        }

        /// <summary>Sets rotation/position directly from (look-at, pitch, distance) and mirrors the result into currentPitch/currentLookAt/currentDistanceValue -- the single place every focus-phase update funnels through, so those three fields are always accurate for the next FocusOnHabitat call.</summary>
        private void ApplyDirectFraming(Vector3 lookAt, float pitch, float distance)
        {
            currentPitch = pitch;
            transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
            transform.position = lookAt - transform.forward * distance;

            currentLookAt = lookAt;
            currentDistanceValue = distance;
        }

        private void EnterHolding()
        {
            focusHoldRemaining = interactionFocusHoldSeconds;
            focusPhase = FocusPhase.Holding;
        }

        private void EnterZoomOut()
        {
            zoomOutStartLookAt = currentLookAt;
            zoomOutStartDistance = currentDistanceValue;
            zoomOutStartPitch = currentPitch;
            zoomOutElapsed = 0f;
            focusPhase = FocusPhase.ZoomOut;
        }

        /// <summary>
        /// Gentle zoom toward a specific habitat when the player interacts with it (Pet/Feed/Play
        /// tap, see InputRouter.EndGesture), so the resulting reaction reads clearly even once the
        /// zoo has grown large enough that auto-fit framing shows everything at a small size. See
        /// FocusPhase for how the zoom-in/hold/zoom-out sequence is timed. Distance never goes
        /// further out than the current auto-fit distance (see UpdateZoomIn's Mathf.Min) -- this
        /// is purely a zoom-in aid, never a step backward.
        ///
        /// Re-triggering on a genuinely DIFFERENT habitat mid-focus (or while easing back out from
        /// a previous one) always seeds the new ZoomIn from currentLookAt/currentDistanceValue/
        /// currentPitch -- kept accurate every frame regardless of phase -- so it flows smoothly
        /// from wherever the camera actually is instead of popping.
        ///
        /// Re-triggering on the SAME habitat already focused (e.g. Pet then Feed then Pet again,
        /// all on one animal while still zoomed in -- a very common pattern) instead just refreshes
        /// the hold timer without restarting ZoomIn from scratch: restarting it would snap
        /// OcclusionFade01 back to 0 for the whole distance leg (since that only ramps up in
        /// ZoomIn's tail), which very visibly un-fades every backgrounded habitat and re-fades them
        /// a moment later even though the camera barely needs to move at all.
        /// </summary>
        public void FocusOnHabitat(Vector3 habitatWorldCenter)
        {
            bool sameTarget = focusPhase != FocusPhase.None && Vector3.Distance(habitatWorldCenter, focusLookAtTarget) < 0.01f;
            focusLookAtTarget = habitatWorldCenter;

            if (sameTarget)
            {
                if (focusPhase == FocusPhase.Holding)
                {
                    focusHoldRemaining = interactionFocusHoldSeconds;
                }
                else if (focusPhase == FocusPhase.ZoomOut)
                {
                    // Already easing back out toward this same habitat's resting view -- reverse
                    // straight back into holding it (wherever the camera currently sits, even if
                    // not fully back to the original focus distance/angle yet) rather than
                    // continuing to ease away and then having to zoom back in all over again.
                    EnterHolding();
                }
                // else already mid-ZoomIn toward this same target -- let it keep going as-is.
                return;
            }

            zoomInStartLookAt = currentLookAt;
            zoomInStartDistance = currentDistanceValue;
            zoomInStartPitch = currentPitch;
            zoomInElapsed = 0f;

            focusPhase = FocusPhase.ZoomIn;
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
