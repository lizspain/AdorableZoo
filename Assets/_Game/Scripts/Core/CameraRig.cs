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
    /// Never touches rotation -- "fixed angle" is whatever the camera's Transform is set to when
    /// this starts (the placeholder angle set manually before this phase), captured once and kept.
    /// Pan directions are plain world +/-X and +/-Z, which only lines up with screen left/right/
    /// up/down because the fixed angle has zero yaw (pure X-axis tilt) -- if that ever changes,
    /// these would need to become camera-relative instead of world-axis-aligned.
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
        [SerializeField] private float paddingWorldUnits = 1.5f;
        [Tooltip("Closest the camera will dolly in, even for a single habitat.")]
        [SerializeField] private float minDistance = 4f;
        [Tooltip("How quickly the camera eases toward a new distance/look-at target.")]
        [SerializeField] private float easeSpeed = 3f;

        [Header("Grid Ceiling (design doc: 5 columns x 3 rows)")]
        [SerializeField] private int ceilingColumns = 5;
        [SerializeField] private int ceilingRows = 3;

        [Header("Panning (beyond the ceiling only)")]
        [Tooltip("World units per second a held pan bar moves the view.")]
        [SerializeField] private float panSpeed = 6f;
        [Tooltip("How close to a pan bound counts as 'already there' for hiding that direction's bar.")]
        [SerializeField] private float panEpsilon = 0.05f;

        private Camera cam;
        private Vector3 fixedForward;
        private float ceilingDistance;
        private float currentDistance;
        private Vector3 lookAtTarget;

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
            fixedForward = transform.forward;

            // Reproduces the camera's current position exactly (lookAtTarget - forward*distance
            // == transform.position), so Update()'s ease has zero delta to start with -- the
            // placeholder position/angle set before this phase holds as-is until real content
            // exists to frame.
            currentDistance = 10f;
            lookAtTarget = transform.position + fixedForward * currentDistance;
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
            var targetPosition = lookAtTarget - fixedForward * currentDistance;
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * easeSpeed);
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

        /// <summary>Distance along fixedForward needed so a flat worldSize (X width, Y=world-Z depth) rectangle fits within the camera's FOV in both axes.</summary>
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
