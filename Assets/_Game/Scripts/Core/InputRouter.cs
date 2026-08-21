using RainbowZoo.Animals;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RainbowZoo.Core
{
    /// <summary>
    /// Resolves raw pointer input into Pet/Feed taps (Play's tap-and-hold-and-drag arrives in
    /// Stage B alongside the shared Toy), enforcing the screen-edge dead zone before any
    /// per-animal logic runs. Built on the Input System's device-agnostic Pointer so the same
    /// code path drives mouse-in-Editor and touch-on-device testing.
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

        private bool gestureActive;
        private Vector2 pressScreenPos;
        private float pressTime;
        private AnimalController pressedAnimal;
        private HabitatRuntime pressedFoodDishHabitat;

        private void Awake()
        {
            if (worldCamera == null) worldCamera = Camera.main;
        }

        private void Update()
        {
            var pointer = Pointer.current;
            if (pointer == null || worldCamera == null) return;

            if (!gestureActive && pointer.press.wasPressedThisFrame)
            {
                BeginGesture(pointer.position.ReadValue());
            }
            else if (gestureActive && pointer.press.wasReleasedThisFrame)
            {
                EndGesture(pointer.position.ReadValue());
            }
        }

        private void BeginGesture(Vector2 screenPos)
        {
            if (IsInDeadZone(screenPos)) return;

            gestureActive = true;
            pressScreenPos = screenPos;
            pressTime = Time.time;
            pressedAnimal = null;
            pressedFoodDishHabitat = null;

            var ray = worldCamera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out var hit, 500f, ~0, QueryTriggerInteraction.Collide)) return;

            pressedAnimal = hit.collider.GetComponentInParent<AnimalController>();
            if (pressedAnimal != null) return;

            var habitat = hit.collider.GetComponentInParent<HabitatRuntime>();
            if (habitat != null && habitat.FoodDish != null && hit.collider.transform == habitat.FoodDish)
            {
                pressedFoodDishHabitat = habitat;
            }
        }

        private void EndGesture(Vector2 screenPos)
        {
            gestureActive = false;

            float duration = Time.time - pressTime;
            float movement = Vector2.Distance(pressScreenPos, screenPos);
            bool isTap = duration <= tapMaxDurationSeconds && movement <= tapMaxMovementPixels;

            if (isTap && pressedFoodDishHabitat != null)
            {
                pressedFoodDishHabitat.Animal?.TryFeed();
            }
            else if (isTap && pressedAnimal != null)
            {
                pressedAnimal.TryPet();
            }

            pressedAnimal = null;
            pressedFoodDishHabitat = null;
        }

        private bool IsInDeadZone(Vector2 screenPos)
        {
            return screenPos.x < screenEdgeDeadZonePixels || screenPos.x > Screen.width - screenEdgeDeadZonePixels
                || screenPos.y < screenEdgeDeadZonePixels || screenPos.y > Screen.height - screenEdgeDeadZonePixels;
        }
    }
}
