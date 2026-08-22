using RainbowZoo.Animals;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RainbowZoo.Core
{
    /// <summary>
    /// Resolves raw pointer input into Pet/Play/Feed, enforcing the screen-edge dead zone before
    /// any per-animal logic runs. Built on the Input System's device-agnostic Pointer so the same
    /// code path drives mouse-in-Editor and touch-on-device testing.
    ///
    /// Pressing directly on the animal's body or the food dish always resolves to Pet/Feed on
    /// release, regardless of hold duration or drag distance -- Play never claims those presses.
    /// A press starting on empty habitat floor (neither the animal nor the dish) is a Play
    /// candidate: once it exceeds the tap duration/movement thresholds (and that habitat's own
    /// Toy is free -- each habitat has its own, so Play on one never blocks Play on another), it
    /// becomes a hold, following the touch on the habitat's ground plane until release, then
    /// throwing the toy proportional to the total drag distance.
    ///
    /// Single-touch-only (doc, section 4): Pointer.current already collapses mouse/touch into
    /// one current pointer for the common single-finger/single-cursor case this drives today.
    /// Explicitly *rejecting* a second simultaneous finger while the first is still down needs
    /// Touchscreen.current's per-touch array, which hasn't been exercised without a physical
    /// touch device -- verify this specifically during device QA (Phase 11).
    /// </summary>
    public sealed class InputRouter : MonoBehaviour
    {
        [SerializeField] private Camera worldCamera;
        [SerializeField] private float screenEdgeDeadZonePixels = 40f;
        [SerializeField] private float tapMaxDurationSeconds = 0.3f;
        [SerializeField] private float tapMaxMovementPixels = 20f;
        [Tooltip("Height above the habitat's ground plane the toy is held at while dragging.")]
        [SerializeField] private float dragHeightOffset = 0.3f;
        [Tooltip("Kept clear of the habitat's actual walls (HabitatRuntime.HalfExtent) so the held toy never visually clips into them.")]
        [SerializeField] private float dragClampMargin = 0.3f;
        [Tooltip("Scales the total drag distance (hold start -> release point) into throw speed -- the farther the drag, the harder the throw.")]
        [SerializeField] private float dragThrowForceMultiplier = 3f;
        [SerializeField] private float throwUpwardBoost = 1.5f;
        [SerializeField] private float maxThrowSpeed = 6f;

        // Habitat walls sit on Ignore Raycast (HabitatPrefabBuilder) since they exist purely as
        // physical containment for the thrown Toy, never as an interaction target -- excluding
        // that layer here is the other half of making that structural. Set in Awake(), not as a
        // static/field initializer -- Unity doesn't allow LayerMask.NameToLayer to be called from
        // a MonoBehaviour's static initializer context.
        private int raycastMask;

        private bool gestureActive;
        private bool playModeActive;
        private Vector2 pressScreenPos;
        private float pressTime;
        private AnimalController pressedAnimal;
        private HabitatRuntime pressedHabitat;
        private HabitatRuntime pressedFoodDishHabitat;
        private Vector3 holdStartPoint;
        private Vector3 lastDragPoint;

        private void Awake()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            raycastMask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
        }

        private void Update()
        {
            var pointer = Pointer.current;
            if (pointer == null || worldCamera == null) return;

            if (!gestureActive)
            {
                if (pointer.press.wasPressedThisFrame)
                {
                    BeginGesture(pointer.position.ReadValue());
                }
                return;
            }

            var screenPos = pointer.position.ReadValue();

            if (!playModeActive)
            {
                TryPromoteToPlayHold(screenPos);
            }
            else
            {
                DragToy(screenPos);
            }

            if (pointer.press.wasReleasedThisFrame)
            {
                EndGesture(screenPos);
            }
        }

        private void BeginGesture(Vector2 screenPos)
        {
            if (IsInDeadZone(screenPos)) return;

            gestureActive = true;
            playModeActive = false;
            pressScreenPos = screenPos;
            pressTime = Time.time;
            pressedAnimal = null;
            pressedHabitat = null;
            pressedFoodDishHabitat = null;

            var ray = worldCamera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out var hit, 500f, raycastMask, QueryTriggerInteraction.Collide)) return;

            pressedHabitat = hit.collider.GetComponentInParent<HabitatRuntime>();
            pressedAnimal = hit.collider.GetComponentInParent<AnimalController>();

            if (pressedHabitat != null && pressedHabitat.FoodDish != null && hit.collider.transform == pressedHabitat.FoodDish)
            {
                pressedFoodDishHabitat = pressedHabitat;
            }
        }

        private void TryPromoteToPlayHold(Vector2 screenPos)
        {
            // Pressing the animal directly is always Pet, never upgrades to Play, regardless of
            // hold duration or drag distance -- Play only starts from empty habitat floor, well
            // clear of the animal's own body collider. Without this, an ordinary Pet click that
            // drifted a few pixels or lasted a touch long (easy to do by accident) would silently
            // launch the several-seconds-long Toy sequence instead of the instant Pet reaction.
            if (pressedAnimal != null) return;
            if (pressedHabitat == null || pressedFoodDishHabitat != null) return;

            var toy = pressedHabitat.Toy;
            if (toy == null || toy.IsBusy) return;

            float duration = Time.time - pressTime;
            float movement = Vector2.Distance(pressScreenPos, screenPos);
            bool becameHold = duration > tapMaxDurationSeconds || movement > tapMaxMovementPixels;
            if (!becameHold) return;

            var worldPoint = ProjectToHabitatPlane(pressedHabitat, screenPos);
            if (!worldPoint.HasValue) return;

            toy.BeginHold(worldPoint.Value);
            playModeActive = true;
            holdStartPoint = worldPoint.Value;
            lastDragPoint = worldPoint.Value;
        }

        private void DragToy(Vector2 screenPos)
        {
            var worldPoint = ProjectToHabitatPlane(pressedHabitat, screenPos);
            if (!worldPoint.HasValue) return;

            pressedHabitat.Toy?.UpdateHoldPosition(worldPoint.Value);
            lastDragPoint = worldPoint.Value;
        }

        private void EndGesture(Vector2 screenPos)
        {
            gestureActive = false;

            if (playModeActive)
            {
                playModeActive = false;
                // Throw force is a function of total drag distance (start of hold -> release),
                // not last-frame velocity -- a slow, long drag throws just as hard as a fast
                // flick covering the same distance, which reads more predictably to a child than
                // velocity-based flicking would.
                var dragDelta = lastDragPoint - holdStartPoint;
                var throwVelocity = dragDelta * dragThrowForceMultiplier + Vector3.up * throwUpwardBoost;
                pressedHabitat.Toy?.Release(Vector3.ClampMagnitude(throwVelocity, maxThrowSpeed));
            }
            else if (pressedFoodDishHabitat != null)
            {
                // Unconditional, not tap-gated: Play can never claim a press that started on the
                // dish or the animal's body (see TryPromoteToPlayHold), so any release reaching
                // here from one of those presses should resolve as Feed/Pet regardless of how
                // long it was held or how far it drifted while still over the target.
                pressedFoodDishHabitat.Animal?.TryFeed();
            }
            else if (pressedAnimal != null)
            {
                pressedAnimal.TryPet();
            }

            pressedAnimal = null;
            pressedHabitat = null;
            pressedFoodDishHabitat = null;
        }

        /// <summary>
        /// Ray-plane intersection at the habitat's ground height, clamped to stay within the
        /// habitat's actual footprint (HabitatRuntime.HalfExtent, minus a small margin so it
        /// doesn't visually clip into the walls). Without this, a drag that leaves the habitat
        /// (or the screen) keeps following the cursor arbitrarily far outside it, and the
        /// eventual throw fires from wherever that was -- often outside the walls entirely.
        /// </summary>
        private Vector3? ProjectToHabitatPlane(HabitatRuntime habitat, Vector2 screenPos)
        {
            float planeHeight = habitat.transform.position.y + dragHeightOffset;
            var plane = new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f));
            var ray = worldCamera.ScreenPointToRay(screenPos);
            if (!plane.Raycast(ray, out float distance)) return null;

            var point = ray.GetPoint(distance);
            var center = habitat.transform.position;
            float limit = HabitatRuntime.HalfExtent - dragClampMargin;
            float clampedX = Mathf.Clamp(point.x - center.x, -limit, limit);
            float clampedZ = Mathf.Clamp(point.z - center.z, -limit, limit);

            return new Vector3(center.x + clampedX, point.y, center.z + clampedZ);
        }

        private bool IsInDeadZone(Vector2 screenPos)
        {
            return screenPos.x < screenEdgeDeadZonePixels || screenPos.x > Screen.width - screenEdgeDeadZonePixels
                || screenPos.y < screenEdgeDeadZonePixels || screenPos.y > Screen.height - screenEdgeDeadZonePixels;
        }
    }
}
