using RainbowZoo.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace RainbowZoo.UI
{
    /// <summary>
    /// The shared Care Meter's on-screen fill bar (design doc section 6/7) -- top-center, just
    /// below where the World Pan Bars sit once visible (ZooNavigationBars.uss). Purely a display:
    /// ZooManager stays the sole writer of ZooCareMeterState (one-way data flow rule); this only
    /// ever reads it, via ZooManager.OnCareHeartsChanged plus a one-time catch-up read at Start
    /// (a restored save can already have partial progress before this runs, and the event only
    /// fires on the NEXT change from here on).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CareMeterUIController : MonoBehaviour
    {
        private UIDocument document;
        private VisualElement fill;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            fill = document.rootVisualElement.Q<VisualElement>("care-meter-fill");
        }

        private void Start()
        {
            if (ZooManager.Instance == null) return;

            ZooManager.Instance.OnCareHeartsChanged += SetFill;
            var state = ZooManager.Instance.CareMeterState;
            SetFill(state.currentHearts, state.currentThreshold);
        }

        private void OnDestroy()
        {
            if (ZooManager.Instance != null)
            {
                ZooManager.Instance.OnCareHeartsChanged -= SetFill;
            }
        }

        private void SetFill(int currentHearts, int threshold)
        {
            if (fill == null || threshold <= 0) return;

            float fraction = Mathf.Clamp01((float)currentHearts / threshold);
            fill.style.width = new StyleLength(new Length(fraction * 100f, LengthUnit.Percent));
        }
    }
}
