using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using RainbowZoo.Core;

namespace RainbowZoo.UI
{
    /// <summary>
    /// Phase 11 Tableau Refinement: each offer slot shows the candidate's actual animated 3D
    /// model instead of its name (design doc goal -- playable by a non-reader) -- idling on a
    /// pedestal while shown, playing its Jump/celebrate animation on selection before the tableau
    /// disappears. UI Toolkit can't render 3D content inline, so each slot gets its own small
    /// preview Camera rendering a pedestal-instantiated copy of the AnimalPrefab into a
    /// RenderTexture bound to that slot's Image. Preview instances need none of the gameplay
    /// apparatus (AnimalController/ControllerPetZoo/NavMeshAgent) -- Idle is already every
    /// species' Animator's default state (the same Idle/Eat/Move/Jump/Rest contract every
    /// controller in this project follows, including the hand-built mythical ones), so it plays
    /// automatically the moment the model is instantiated; celebrate-on-select is just
    /// Animator.SetTrigger("jump"), the same parameter name ControllerPetZoo itself uses.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OfferTableauController : MonoBehaviour
    {
        private const string PreviewLayerName = "TableauPreview";
        private const int RenderTextureSize = 256;
        private const float PedestalSpacing = 30f;
        private const float CelebrateSecondsBeforePlacing = 0.8f;
        private static readonly Vector3 PedestalAreaOrigin = new Vector3(500f, 50f, 0f);

        // #E8D9BE (--color-birch-base, theme.uss), opaque -- the preview camera's backdrop, so an
        // uninstantiated corner of the render texture reads as "matches the card" rather than a
        // stray solid color.
        private static readonly Color PedestalBackgroundColor = new Color(0.9098039f, 0.8509804f, 0.74509805f, 1f);

        private sealed class SlotPreview
        {
            public Camera Camera;
            public RenderTexture RenderTexture;
            public GameObject ModelInstance;
            public Animator Animator;
        }

        private UIDocument document;
        private VisualElement root;
        private readonly Button[] slotButtons = new Button[OfferTableau.SlotCount];
        private readonly Image[] slotImages = new Image[OfferTableau.SlotCount];
        private readonly SlotPreview[] slotPreviews = new SlotPreview[OfferTableau.SlotCount];
        private OfferTableau currentTableau;
        private int previewLayer;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
            previewLayer = LayerMask.NameToLayer(PreviewLayerName);
            if (previewLayer < 0)
            {
                Debug.LogError($"[OfferTableauController] Layer '{PreviewLayerName}' doesn't exist -- add it in Project Settings > Tags and Layers. Tableau previews will not render.", this);
            }
        }

        private void OnEnable()
        {
            root = document.rootVisualElement.Q<VisualElement>("offer-tableau-root");
            if (root == null)
            {
                Debug.LogError("OfferTableau.uxml is missing the 'offer-tableau-root' VisualElement -- check the UXML loaded correctly.", this);
                return;
            }

            // The UXML/USS don't set an initial display value (defaults to visible), so without
            // this, resuming a save -- which never calls ShowTableau on boot, see ZooManager.Start
            // -- would leave an empty, unpopulated tableau panel sitting visible over the zoo.
            root.style.display = DisplayStyle.None;

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

                slotImages[i] = button.Q<Image>($"slot-{i}-image");
                if (slotImages[i] == null)
                {
                    Debug.LogError($"OfferTableau.uxml is missing an Image named 'slot-{i}-image' inside 'slot-{i}' -- check the UXML loaded correctly.", this);
                }
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
            ClearAllPreviews();
        }

        private void ShowTableau(OfferTableau tableau)
        {
            currentTableau = tableau;
            ClearAllPreviews();

            for (int i = 0; i < OfferTableau.SlotCount; i++)
            {
                var button = slotButtons[i];
                if (button == null) continue;

                var definition = tableau.GetSlot(i);
                if (definition == null)
                {
                    button.SetEnabled(false);
                    continue;
                }

                button.SetEnabled(true);
                button.EnableInClassList("offer-slot--mythical", definition.IsMythical);
                SpawnPreview(i, definition);
            }

            if (root != null) root.style.display = DisplayStyle.Flex;

            AudioDirector.Instance?.PlayTableauFanfare();
        }

        private void SpawnPreview(int slotIndex, AnimalDefinition definition)
        {
            if (definition.AnimalPrefab == null || previewLayer < 0) return;

            var pedestalPosition = PedestalAreaOrigin + Vector3.right * (slotIndex * PedestalSpacing);

            var modelInstance = Instantiate(definition.AnimalPrefab, pedestalPosition, Quaternion.identity);
            modelInstance.name = $"[TableauPreview] {definition.Id}";
            SetLayerRecursively(modelInstance.transform, previewLayer);

            // Static pedestal display only -- no movement or interaction happens here, so none of
            // the gameplay scripts need to run. The Animator itself is a built-in engine
            // Behaviour, not a MonoBehaviour, so this loop leaves it untouched and it keeps
            // evaluating its own state machine normally (starting on Idle, its default state).
            foreach (var behaviour in modelInstance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                behaviour.enabled = false;
            }
            foreach (var agent in modelInstance.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
            {
                agent.enabled = false;
            }

            var animator = modelInstance.GetComponentInChildren<Animator>();

            var renderTexture = new RenderTexture(RenderTextureSize, RenderTextureSize, 16)
            {
                name = $"TableauPreviewRT_{slotIndex}"
            };

            var cameraGo = new GameObject($"[TableauPreviewCamera] {slotIndex}");
            cameraGo.transform.SetParent(transform, false);
            var camera = cameraGo.AddComponent<Camera>();
            camera.cullingMask = 1 << previewLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PedestalBackgroundColor;
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.05f;
            camera.targetTexture = renderTexture;

            FrameCameraOnModel(camera, modelInstance);

            if (slotImages[slotIndex] != null)
            {
                slotImages[slotIndex].image = renderTexture;
            }

            slotPreviews[slotIndex] = new SlotPreview
            {
                Camera = camera,
                RenderTexture = renderTexture,
                ModelInstance = modelInstance,
                Animator = animator
            };
        }

        /// <summary>Frames modelInstance's actual rendered bounds rather than a fixed distance/FOV -- species vary wildly in size (a Mermaid vs. a Whale), so no single constant fits all of them.</summary>
        private static void FrameCameraOnModel(Camera camera, GameObject modelInstance)
        {
            var renderers = modelInstance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                // +Z, not -Z -- these rigs face -Z (confirmed by testing: the original -Z eye
                // position framed every animal's backside, exactly 180 degrees off), so the
                // camera has to sit on the +Z side to look at their front.
                camera.transform.position = modelInstance.transform.position + new Vector3(0f, 1f, 3f);
                camera.transform.LookAt(modelInstance.transform.position + Vector3.up * 0.5f);
                return;
            }

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float radius = bounds.extents.magnitude;
            float halfFovRad = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            // Tightened from 1.15 -> 1.05 (still a small buffer for the Idle loop's own motion,
            // since bounds are only sampled once at spawn) after feedback that animals read a
            // bit small on the tableau.
            float distance = (radius / Mathf.Sin(halfFovRad)) * 1.05f;

            // Small 3/4-angle orbit around the subject instead of framing dead-on -- feedback
            // that a straight-on face-first view hides too much of each animal's form. Rotates
            // the *position* the camera orbits to (LookAt below still keeps it aimed at center),
            // not the camera's own facing the way CameraRig.pitchDegrees does, so the sign
            // conventions aren't directly comparable to that field. Picked without being able to
            // see the render -- if "up"/"left" come out backwards or as "down"/"right" instead,
            // flip the corresponding constant's sign.
            const float orbitPitchDegrees = 15f;
            const float orbitYawDegrees = 15f;
            var orbitRotation = Quaternion.Euler(-orbitPitchDegrees, -orbitYawDegrees, 0f);
            var eye = bounds.center + orbitRotation * (Vector3.forward * distance);
            camera.transform.position = eye;
            camera.transform.LookAt(bounds.center);
        }

        private static void SetLayerRecursively(Transform node, int layer)
        {
            node.gameObject.layer = layer;
            foreach (Transform child in node)
            {
                SetLayerRecursively(child, layer);
            }
        }

        private void ClearAllPreviews()
        {
            for (int i = 0; i < slotPreviews.Length; i++)
            {
                ClearPreview(i);
            }
        }

        private void ClearPreview(int slotIndex)
        {
            var preview = slotPreviews[slotIndex];
            if (preview == null) return;

#if UNITY_EDITOR
            // Dev-inspector nicety only, stripped from real builds: if one of these dynamically
            // created preview objects happens to be selected in the Hierarchy when destroyed, the
            // Inspector throws a MissingReferenceException trying to redraw its now-null target.
            // Harmless (Selection doesn't exist outside the Editor), but noisy in the Console.
            bool previewCameraSelected = preview.Camera != null && UnityEditor.Selection.activeGameObject == preview.Camera.gameObject;
            if (UnityEditor.Selection.activeGameObject == preview.ModelInstance || previewCameraSelected)
            {
                UnityEditor.Selection.activeGameObject = null;
            }
#endif

            if (preview.ModelInstance != null) Destroy(preview.ModelInstance);
            if (preview.Camera != null) Destroy(preview.Camera.gameObject);
            if (preview.RenderTexture != null) preview.RenderTexture.Release();

            slotPreviews[slotIndex] = null;
        }

        private void OnSlotClicked(int slotIndex)
        {
            if (currentTableau == null) return;
            var definition = currentTableau.GetSlot(slotIndex);
            if (definition == null) return;

            // Disable every slot immediately so a second tap can't land mid-celebration.
            for (int i = 0; i < slotButtons.Length; i++)
            {
                slotButtons[i]?.SetEnabled(false);
            }

            StartCoroutine(CelebrateThenPlace(slotIndex, definition));
        }

        /// <summary>Doc: "tapping ... the tableau disappears and the habitat materializes" -- now with the selected animal's own Jump/celebrate beat playing first, so the choice reads as a little celebration of its own before it moves into the zoo.</summary>
        private IEnumerator CelebrateThenPlace(int slotIndex, AnimalDefinition definition)
        {
            slotPreviews[slotIndex]?.Animator?.SetTrigger("jump");

            yield return new WaitForSeconds(CelebrateSecondsBeforePlacing);

            if (root != null) root.style.display = DisplayStyle.None;
            ClearAllPreviews();

            ZooManager.Instance.PlaceAnimal(definition);
        }
    }
}
