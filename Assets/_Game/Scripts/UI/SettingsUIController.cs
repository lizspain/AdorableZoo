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
        private const float HoldFillStart = 0.25f;

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

            // OnEnable can run again after a disable/enable cycle -- only add the icon once.
            if (gate.Q<GearIcon>() == null)
            {
                gate.Add(new GearIcon());
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
            // Show the fill the instant the press lands, so a tap visibly "registers" at once.
            SetFill(HoldFillStart);
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
            // Runs from HoldFillStart (the press) up to full at HoldSecondsToActivate.
            SetFill(Mathf.Lerp(HoldFillStart, 1f, Mathf.Clamp01(holdElapsed / HoldSecondsToActivate)));

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

        /// <summary>
        /// The gear on the gate peg, in the Figma Make kit's "dark carved" marking style: a dark
        /// brown cut (#4E3028, solid) with a faint light lip just under it, as if routed into the
        /// birch. Drawn with Painter2D -- no texture asset needed. The gate's USS opacity applies
        /// on top of this, so the icon stays as subdued as the rest of the parent-facing control.
        /// </summary>
        private sealed class GearIcon : VisualElement
        {
            private const int Teeth = 8;
            private const float IconFillOfBox = 0.80f;   // gear diameter as a share of the gate's inner box
            private const float RootRadius = 0.72f;      // tooth base, as a share of the outer radius
            private const float HoleRadius = 0.30f;
            private const float ToothBaseHalfDegrees = 14f;
            private const float ToothTipHalfDegrees = 9f;

            private static readonly Color CarvedColor = new Color(0x4E / 255f, 0x30 / 255f, 0x28 / 255f, 1f);
            private static readonly Color LipColor = new Color(1f, 1f, 1f, 0.5f);

            public GearIcon()
            {
                name = "settings-gate-icon";
                pickingMode = PickingMode.Ignore; // the gate's own hold gesture must see every press
                style.position = Position.Absolute;
                style.left = 0f;
                style.top = 0f;
                style.right = 0f;
                style.bottom = 0f;
                generateVisualContent += OnGenerateVisualContent;
            }

            private void OnGenerateVisualContent(MeshGenerationContext mgc)
            {
                var rect = contentRect;
                if (rect.width <= 0f || rect.height <= 0f) return;

                float outerRadius = Mathf.Min(rect.width, rect.height) * 0.5f * IconFillOfBox;
                var painter = mgc.painter2D;

                DrawGear(painter, rect.center + new Vector2(0f, 1f), outerRadius, LipColor);
                DrawGear(painter, rect.center, outerRadius, CarvedColor);
            }

            private static void DrawGear(Painter2D painter, Vector2 center, float outerRadius, Color color)
            {
                float rootRadius = outerRadius * RootRadius;

                painter.fillColor = color;
                painter.BeginPath();

                bool first = true;
                for (int tooth = 0; tooth < Teeth; tooth++)
                {
                    float angle = tooth * 360f / Teeth;
                    AddPoint(painter, center, rootRadius, angle - ToothBaseHalfDegrees, ref first);
                    AddPoint(painter, center, outerRadius, angle - ToothTipHalfDegrees, ref first);
                    AddPoint(painter, center, outerRadius, angle + ToothTipHalfDegrees, ref first);
                    AddPoint(painter, center, rootRadius, angle + ToothBaseHalfDegrees, ref first);
                }
                painter.ClosePath();

                // The axle hole is a second subpath in the same fill; even-odd leaves it empty.
                float holeRadius = outerRadius * HoleRadius;
                painter.MoveTo(center + new Vector2(holeRadius, 0f));
                painter.Arc(center, holeRadius, Angle.Degrees(0f), Angle.Degrees(360f));
                painter.ClosePath();

                painter.Fill(FillRule.OddEven);
            }

            private static void AddPoint(Painter2D painter, Vector2 center, float radius, float degrees, ref bool first)
            {
                float radians = degrees * Mathf.Deg2Rad;
                var point = center + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius;
                if (first)
                {
                    painter.MoveTo(point);
                    first = false;
                }
                else
                {
                    painter.LineTo(point);
                }
            }
        }
    }
}
