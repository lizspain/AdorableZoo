using RainbowZoo.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace RainbowZoo.UI
{
    /// <summary>
    /// The four World Pan Bars (design doc section 14): hidden entirely while CameraRig's
    /// auto-zoom can still fit everything, shown once it's holding at the 5x3 ceiling. Each bar
    /// is further hidden ("not merely disabled") in whichever direction panning would show past
    /// the outer edge of placed content -- matched here via display:none, which (like
    /// SetActive(false)) removes it from layout and picking entirely, not just visually.
    /// Press-and-hold pans continuously; releasing (including dragging off the bar) stops it.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ZooNavigationBarsController : MonoBehaviour
    {
        private UIDocument document;
        private VisualElement barLeft, barRight, barTop, barBottom;
        private bool holdingLeft, holdingRight, holdingTop, holdingBottom;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            var root = document.rootVisualElement;
            barLeft = root.Q<VisualElement>("bar-left");
            barRight = root.Q<VisualElement>("bar-right");
            barTop = root.Q<VisualElement>("bar-top");
            barBottom = root.Q<VisualElement>("bar-bottom");

            RegisterHold(barLeft, held => holdingLeft = held);
            RegisterHold(barRight, held => holdingRight = held);
            RegisterHold(barTop, held => holdingTop = held);
            RegisterHold(barBottom, held => holdingBottom = held);
        }

        private static void RegisterHold(VisualElement element, System.Action<bool> setHeld)
        {
            if (element == null) return;
            element.RegisterCallback<PointerDownEvent>(_ => setHeld(true));
            element.RegisterCallback<PointerUpEvent>(_ => setHeld(false));
            element.RegisterCallback<PointerLeaveEvent>(_ => setHeld(false));
        }

        private void Update()
        {
            if (CameraRig.Instance == null) return;

            UpdateBarVisibility(barLeft, CameraRig.Instance.CanPanLeft);
            UpdateBarVisibility(barRight, CameraRig.Instance.CanPanRight);
            UpdateBarVisibility(barTop, CameraRig.Instance.CanPanForward);
            UpdateBarVisibility(barBottom, CameraRig.Instance.CanPanBack);

            float dt = Time.deltaTime;
            if (holdingLeft) CameraRig.Instance.Pan(Vector3.left, dt);
            if (holdingRight) CameraRig.Instance.Pan(Vector3.right, dt);
            if (holdingTop) CameraRig.Instance.Pan(Vector3.forward, dt);
            if (holdingBottom) CameraRig.Instance.Pan(Vector3.back, dt);
        }

        private static void UpdateBarVisibility(VisualElement bar, bool canPan)
        {
            if (bar == null) return;
            bar.style.display = canPan ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
