using RainbowZoo.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace RainbowZoo.UI
{
    /// <summary>
    /// Settings entry point (design doc section 12/14): a small, deliberately inconspicuous icon
    /// tucked in the corner, gated behind a continuous 4-second press-and-hold so it can't be
    /// triggered by a curious child's tap -- releasing early cancels and resets the hold
    /// (mirrors the doc's original Parental Gate intent, simplified to a single hold gesture
    /// rather than the doc's hold-then-separate-tap sequence, per current direction). Opens a
    /// small menu offering the full-roster unlock and Reset Zoo. Reset Zoo still gets its own
    /// "are you sure" confirmation step regardless of how the menu was reached, since it's an
    /// irreversible, destructive action wiping the player's whole zoo.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SettingsUIController : MonoBehaviour
    {
        private const float HoldSecondsToActivate = 4f;

        private UIDocument document;
        private VisualElement gate, gateFill, menu, resetConfirm;
        private bool isHolding;
        private float holdElapsed;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            var root = document.rootVisualElement;
            gate = root.Q<VisualElement>("settings-gate");
            gateFill = root.Q<VisualElement>("settings-gate-fill");
            menu = root.Q<VisualElement>("settings-menu");
            resetConfirm = root.Q<VisualElement>("reset-confirm");

            if (gate == null)
            {
                Debug.LogError("SettingsUI.uxml is missing the 'settings-gate' VisualElement -- check the UXML loaded correctly.", this);
                return;
            }

            gate.RegisterCallback<PointerDownEvent>(_ => StartHold());
            gate.RegisterCallback<PointerUpEvent>(_ => CancelHold());
            gate.RegisterCallback<PointerLeaveEvent>(_ => CancelHold());

            RegisterButton(root, "unlock-button", OnUnlockClicked);
            RegisterButton(root, "reset-button", ShowResetConfirm);
            RegisterButton(root, "close-button", CloseAll);
            RegisterButton(root, "reset-confirm-yes", OnResetConfirmed);
            RegisterButton(root, "reset-confirm-cancel", ShowMenu);
        }

        private void RegisterButton(VisualElement root, string name, System.Action onClick)
        {
            var button = root.Q<Button>(name);
            if (button == null)
            {
                Debug.LogError($"SettingsUI.uxml is missing a Button named '{name}' -- check the UXML loaded correctly.", this);
                return;
            }
            button.clicked += onClick;
        }

        private void StartHold()
        {
            isHolding = true;
            holdElapsed = 0f;
        }

        private void CancelHold()
        {
            isHolding = false;
            holdElapsed = 0f;
            SetFill(0f);
        }

        private void Update()
        {
            if (!isHolding) return;

            holdElapsed += Time.deltaTime;
            SetFill(Mathf.Clamp01(holdElapsed / HoldSecondsToActivate));

            if (holdElapsed >= HoldSecondsToActivate)
            {
                isHolding = false;
                holdElapsed = 0f;
                SetFill(0f);
                ShowMenu();
            }
        }

        private void SetFill(float t)
        {
            if (gateFill != null) gateFill.style.height = new Length(t * 100f, LengthUnit.Percent);
        }

        private void ShowMenu()
        {
            if (menu != null) menu.style.display = DisplayStyle.Flex;
            if (resetConfirm != null) resetConfirm.style.display = DisplayStyle.None;
        }

        private void CloseAll()
        {
            if (menu != null) menu.style.display = DisplayStyle.None;
            if (resetConfirm != null) resetConfirm.style.display = DisplayStyle.None;
        }

        private void ShowResetConfirm()
        {
            if (menu != null) menu.style.display = DisplayStyle.None;
            if (resetConfirm != null) resetConfirm.style.display = DisplayStyle.Flex;
        }

        private void OnUnlockClicked()
        {
            ZooManager.Instance?.UnlockFullRoster();
            CloseAll();
        }

        private void OnResetConfirmed()
        {
            CloseAll();
            ZooManager.Instance?.ResetZoo();
        }
    }
}
