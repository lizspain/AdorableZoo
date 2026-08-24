using RainbowZoo.Animals;
using UnityEngine;

namespace RainbowZoo.Core
{
    /// <summary>
    /// Off-camera simplification for one placed habitat (design doc section 13, Phase 9): while
    /// this habitat's bounds fall outside the main camera's frustum, its physical containment
    /// Walls are disabled (their only job is constraining the thrown Toy's Rigidbody -- nothing to
    /// constrain while no one can throw the toy here) and its AnimalController's wander/audio
    /// polling is paused (AnimalController.SetSimplified). Checked on a periodic timer rather than
    /// every frame -- visibility doesn't need per-frame precision, and this runs once per placed
    /// habitat, so an every-frame check would scale with zoo size for no benefit.
    /// </summary>
    [RequireComponent(typeof(HabitatRuntime))]
    public sealed class HabitatVisibilityLod : MonoBehaviour
    {
        private const float CheckIntervalSeconds = 0.25f;

        private HabitatRuntime habitatRuntime;
        private Transform walls;
        private Bounds worldBounds;
        private float checkTimer;

        public bool IsVisible { get; private set; } = true;

        private void Awake()
        {
            habitatRuntime = GetComponent<HabitatRuntime>();
            walls = transform.Find("Walls");

            float half = HabitatRuntime.HalfExtent;
            worldBounds = new Bounds(transform.position + Vector3.up, new Vector3(half * 2f, 2f, half * 2f));

            // Staggered start so every habitat placed in the same frame doesn't also re-check
            // visibility on the exact same frame forever after.
            checkTimer = Random.Range(0f, CheckIntervalSeconds);
        }

        private void Update()
        {
            checkTimer -= Time.deltaTime;
            if (checkTimer > 0f) return;
            checkTimer = CheckIntervalSeconds;

            var cam = Camera.main;
            bool visible = cam == null || IsWithinFrustum(cam);
            if (visible == IsVisible) return;

            IsVisible = visible;
            ApplyVisibility(visible);
        }

        private bool IsWithinFrustum(Camera cam)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            return GeometryUtility.TestPlanesAABB(planes, worldBounds);
        }

        private void ApplyVisibility(bool visible)
        {
            if (walls != null) walls.gameObject.SetActive(visible);
            habitatRuntime.Animal?.SetSimplified(!visible);
        }
    }
}
