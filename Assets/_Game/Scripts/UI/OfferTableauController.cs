using UnityEngine;
using UnityEngine.UIElements;
using RainbowZoo.Core;

namespace RainbowZoo.UI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class OfferTableauController : MonoBehaviour
    {
        private UIDocument document;
        private VisualElement root;
        private readonly Button[] slotButtons = new Button[OfferTableau.SlotCount];
        private OfferTableau currentTableau;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            root = document.rootVisualElement.Q<VisualElement>("offer-tableau-root");
            if (root == null)
            {
                Debug.LogError("OfferTableau.uxml is missing the 'offer-tableau-root' VisualElement -- check the UXML loaded correctly.", this);
                return;
            }

            for (int i = 0; i < OfferTableau.SlotCount; i++)
            {
                var button = root.Q<Button>($"slot-{i}");
                if (button == null)
                {
                    Debug.LogError($"OfferTableau.uxml is missing a Button named 'slot-{i}' -- check the UXML loaded correctly.", this);
                    continue;
                }

                int slotIndex = i;
                button.clicked += () => OnSlotClicked(slotIndex);
                slotButtons[i] = button;
            }
        }

        private void Start()
        {
            // Deferred to Start(): Awake() on every object is guaranteed to run before Start()
            // on any object, so ZooManager.Instance (set in its Awake) is safe to read here --
            // unlike OnEnable(), whose order relative to *other* objects' Awake isn't guaranteed.
            if (ZooManager.Instance == null)
            {
                Debug.LogError("ZooManager.Instance is still null in Start() -- is a ZooManager present in the scene?", this);
                return;
            }

            ZooManager.Instance.OnTableauReady += ShowTableau;

            // Catch up if ZooManager already generated a tableau (e.g. from its own Start())
            // before this subscription happened.
            if (ZooManager.Instance.CurrentTableau != null)
            {
                ShowTableau(ZooManager.Instance.CurrentTableau);
            }
        }

        private void OnDisable()
        {
            if (ZooManager.Instance != null)
            {
                ZooManager.Instance.OnTableauReady -= ShowTableau;
            }
        }

        private void ShowTableau(OfferTableau tableau)
        {
            currentTableau = tableau;
            for (int i = 0; i < OfferTableau.SlotCount; i++)
            {
                var button = slotButtons[i];
                if (button == null) continue;

                var definition = tableau.GetSlot(i);
                if (definition == null)
                {
                    button.text = string.Empty;
                    button.SetEnabled(false);
                    continue;
                }

                button.SetEnabled(true);
                button.text = definition.DisplayName;
                button.EnableInClassList("offer-slot--mythical", definition.IsMythical);
            }

            if (root != null) root.style.display = DisplayStyle.Flex;
        }

        private void OnSlotClicked(int slotIndex)
        {
            if (currentTableau == null) return;
            var definition = currentTableau.GetSlot(slotIndex);
            if (definition == null) return;

            // Doc: "tapping ... the tableau disappears and the habitat materializes." Hide
            // immediately rather than waiting on ZooManager to decide when the next one is due
            // (Phase 5 gates that behind the Care Meter -- until then it's a debug key, see
            // ZooManager.RequestNextTableauDebug).
            if (root != null) root.style.display = DisplayStyle.None;

            ZooManager.Instance.PlaceAnimal(definition);
        }
    }
}
