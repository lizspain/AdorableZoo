using System.Collections.Generic;
using RainbowZoo.Animals;
using UnityEngine;
using UnityEngine.Rendering;

namespace RainbowZoo.Core
{
    /// <summary>
    /// Phase 11 UX refinement: while CameraRig is zooming in/holding/zooming out on a habitat
    /// (OcclusionFade01 > 0), any OTHER placed habitat currently both (a) inside the camera's
    /// frustum and (b) nearer to the camera than the focused habitat -- i.e. actually able to sit
    /// between the camera and the focus target -- fades down to fadedOpacity, so it can't visually
    /// block the animal the player is actually watching. Fades back in as CameraRig eases back
    /// out. Auto-attached to the same GameObject as CameraRig (see CameraRig.Awake) -- no scene
    /// wiring needed.
    ///
    /// Each renderer's OWN displayed alpha eases toward its target (whichever of fadedOpacity/1 it
    /// should currently be heading for) at perRendererFadeSpeed, decoupled from the raw per-frame
    /// frustum+depth classification below -- a renderer only actually drops its proxy and reverts
    /// to the original material once its eased alpha has fully recovered to 1, not the instant it
    /// stops being classified as obscuring. Without this, a habitat sitting right at the frustum or
    /// depth boundary as the camera moves through a transition would classify in/out frame to
    /// frame, and each flip snapped the material straight between "faded proxy" and "original,
    /// fully opaque" -- a hard, distracting blink rather than a fade.
    ///
    /// InputRouter queries IsHabitatFaded (via the static Instance) to reject presses on any
    /// habitat that's currently faded, using that same eased alpha rather than raw classification
    /// so the interaction gate matches what's actually on screen -- but NOT on habitats that simply
    /// aren't the current focus target; anything not visibly faded stays fully interactable.
    ///
    /// Renderer transparency -- TWO attempts at driving the vendor animal materials' own shader
    /// directly (a generic URP Lit Opaque->Transparent switch, then that same switch adapted for
    /// Unity Toon Shader's actual `_Tweak_transparency`/clipping-keyword mechanism after reading
    /// its package source) both produced NO visible change when tested live. Fades instead via a
    /// STAND-IN proxy material: a plain `Universal Render Pipeline/Unlit` material (a stock,
    /// always-alpha-blend-capable shader) carrying the original material's own base texture and
    /// base tint, swapped in only while fading. Trades exact toon shading (rim light, cel bands)
    /// for a flat/unlit look while faded, in exchange for actually working. Each renderer gets ONE
    /// lazily-created, cached proxy per material slot (created on first use, reused after),
    /// restored to the renderer's original SHARED materials -- and Phase 9's GPU-instancing
    /// batching -- the instant it's fully recovered. Renderers never faded are untouched.
    ///
    /// Known follow-up risk (not yet addressed): Shader.Find only reliably succeeds in the Editor.
    /// In a real device build, "Universal Render Pipeline/Unlit" must be listed under Graphics
    /// Settings > Always Included Shaders or it can be stripped since no material in the project
    /// references it directly -- fine for Editor testing now, needs that Graphics Settings entry
    /// before a device build.
    /// </summary>
    public sealed class HabitatOcclusionFader : MonoBehaviour
    {
        public static HabitatOcclusionFader Instance { get; private set; }

        [Tooltip("Alpha (0-1) faded habitats/animals settle at once fully faded -- mirrors CameraRig's interactionFocusOcclusionOpacity default (0.1 = 10%). Kept as its own field here rather than reading CameraRig's so this component stays usable/testable on its own.")]
        [Range(0f, 1f)]
        [SerializeField] private float fadedOpacity = 0.1f;

        [Tooltip("How quickly a renderer's own displayed alpha (0-1 per second, via MoveTowards) eases toward its current target -- decoupled from CameraRig's own fade timing so momentary frustum/depth classification changes as the camera moves smooth out into a barely-perceptible wobble instead of a visible blink.")]
        [SerializeField] private float perRendererFadeSpeed = 6f;

        [Tooltip("Alpha threshold below which a habitat counts as 'currently faded' for InputRouter's interaction gate (IsHabitatFaded) -- deliberately a bit below 1, not exactly 1, so a habitat becomes interactable again slightly before its fade-back animation has pixel-perfect finished.")]
        [Range(0f, 0.99f)]
        [SerializeField] private float fadedInteractionThreshold = 0.9f;

        private static readonly string[] ProxyShaderCandidates =
        {
            "Universal Render Pipeline/Unlit",
            "Unlit/Transparent",
            "Sprites/Default",
        };

        /// <summary>World-unit slack on the "closer to camera than the target" depth test, so the target's own depth and any habitat at essentially the same depth don't flicker in/out from floating-point noise.</summary>
        private const float DepthMargin = 0.05f;

        private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Renderer, Material[]> proxyInstances = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Renderer, GameObject> rendererHabitat = new Dictionary<Renderer, GameObject>();
        private readonly Dictionary<Renderer, float> rendererAlpha = new Dictionary<Renderer, float>();
        private readonly HashSet<GameObject> currentlyFadedHabitats = new HashSet<GameObject>();

        private readonly HashSet<Renderer> obscuringScratch = new HashSet<Renderer>();
        private readonly List<Renderer> renderersScratch = new List<Renderer>();
        private readonly List<Renderer> activeRenderersScratch = new List<Renderer>();
        private readonly List<Renderer> toDropScratch = new List<Renderer>();

        private Shader proxyShader;

        /// <summary>True if this exact habitat root is currently faded enough (below fadedInteractionThreshold) that InputRouter should reject presses on it.</summary>
        public bool IsHabitatFaded(GameObject habitatRoot) => habitatRoot != null && currentlyFadedHabitats.Contains(habitatRoot);

        private void Awake()
        {
            Instance = this;

            foreach (var name in ProxyShaderCandidates)
            {
                proxyShader = Shader.Find(name);
                if (proxyShader != null) break;
            }

            if (proxyShader == null)
            {
                Debug.LogError("[HabitatOcclusionFader] None of the candidate proxy shaders were found -- occlusion fade will be disabled.", this);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            if (proxyShader == null || CameraRig.Instance == null || ZooManager.Instance == null) return;

            float fade01 = CameraRig.Instance.OcclusionFade01;
            var mainCam = Camera.main;

            obscuringScratch.Clear();
            if (fade01 > 0f && mainCam != null)
            {
                ComputeObscuringSet(mainCam);
            }

            float targetAlphaWhenObscuring = Mathf.Lerp(1f, fadedOpacity, fade01);

            // Process the union of "obscuring right now" and "still easing back from an earlier
            // fade" -- snapshotted first since dropping fully-recovered renderers below mutates
            // rendererAlpha while we'd otherwise still be iterating it.
            activeRenderersScratch.Clear();
            activeRenderersScratch.AddRange(obscuringScratch);
            foreach (var renderer in rendererAlpha.Keys)
            {
                if (!obscuringScratch.Contains(renderer)) activeRenderersScratch.Add(renderer);
            }

            toDropScratch.Clear();
            currentlyFadedHabitats.Clear();

            foreach (var renderer in activeRenderersScratch)
            {
                if (renderer == null)
                {
                    toDropScratch.Add(renderer);
                    continue;
                }

                bool isObscuring = obscuringScratch.Contains(renderer);
                float target = isObscuring ? targetAlphaWhenObscuring : 1f;
                float current = rendererAlpha.TryGetValue(renderer, out var existing) ? existing : 1f;
                current = Mathf.MoveTowards(current, target, Time.deltaTime * perRendererFadeSpeed);

                if (!isObscuring && current >= 0.999f)
                {
                    // Fully recovered -- drop the proxy entirely and stop tracking.
                    RestoreRenderer(renderer);
                    toDropScratch.Add(renderer);
                    continue;
                }

                rendererAlpha[renderer] = current;
                ApplyAlpha(renderer, current);

                if (current < fadedInteractionThreshold
                    && rendererHabitat.TryGetValue(renderer, out var habitatRoot) && habitatRoot != null)
                {
                    currentlyFadedHabitats.Add(habitatRoot);
                }
            }

            foreach (var renderer in toDropScratch) rendererAlpha.Remove(renderer);
        }

        /// <summary>
        /// "Closer to the camera" means nearer along the camera's own view axis than the focus
        /// target is -- not just anywhere in frustum. Depth here is the signed distance along
        /// camera-forward from the camera to each point; something with LESS depth than the target
        /// sits between the camera and the target and can actually obscure it, while something
        /// with MORE depth is behind the target and never could, regardless of being in frustum.
        /// </summary>
        private void ComputeObscuringSet(Camera mainCam)
        {
            var focusCenter = CameraRig.Instance.FocusedHabitatCenter;
            var planes = GeometryUtility.CalculateFrustumPlanes(mainCam);
            float half = HabitatRuntime.HalfExtent;

            var camTransform = mainCam.transform;
            Vector3 camPos = camTransform.position;
            Vector3 camForward = camTransform.forward;
            float targetDepth = Vector3.Dot(focusCenter - camPos, camForward);

            foreach (var habitatGo in ZooManager.Instance.InstantiatedHabitats)
            {
                if (habitatGo == null) continue;

                // Habitats never overlap, so a small position tolerance safely identifies "this
                // is the one actually being focused" without needing a direct object reference.
                if (Vector3.Distance(habitatGo.transform.position, focusCenter) < 0.01f) continue;

                float depth = Vector3.Dot(habitatGo.transform.position - camPos, camForward);
                if (depth >= targetDepth - DepthMargin) continue;

                var bounds = new Bounds(habitatGo.transform.position + Vector3.up, new Vector3(half * 2f, 2f, half * 2f));
                if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;

                renderersScratch.Clear();
                habitatGo.GetComponentsInChildren(false, renderersScratch);
                foreach (var renderer in renderersScratch)
                {
                    obscuringScratch.Add(renderer);
                    rendererHabitat[renderer] = habitatGo;
                }
            }
        }

        private void ApplyAlpha(Renderer renderer, float alpha)
        {
            if (!proxyInstances.TryGetValue(renderer, out var proxies))
            {
                var shared = renderer.sharedMaterials;
                originalMaterials[renderer] = shared;

                proxies = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++)
                {
                    proxies[i] = CreateProxy(shared[i]);
                }

                proxyInstances[renderer] = proxies;
            }

            // Re-applied every call, not just the first time a proxy is created for this renderer:
            // RestoreRenderer sets the renderer back to its ORIGINAL materials once fully
            // recovered, so a later re-fade needs this reassigned again even though the cached
            // proxy array itself doesn't need recreating.
            renderer.sharedMaterials = proxies;

            foreach (var proxy in proxies)
            {
                if (proxy == null) continue;
                var color = proxy.HasProperty("_BaseColor") ? proxy.GetColor("_BaseColor") : proxy.color;
                color.a = alpha;
                if (proxy.HasProperty("_BaseColor")) proxy.SetColor("_BaseColor", color); else proxy.color = color;
            }
        }

        private Material CreateProxy(Material original)
        {
            var proxy = new Material(proxyShader) { name = (original != null ? original.name : "Fade") + " (Occlusion Fade Proxy)" };

            Texture mainTex = null;
            Color tint = Color.white;
            if (original != null)
            {
                // Prefer whichever texture slot actually HAS a texture assigned, not whichever
                // property merely EXISTS on the shader -- the vendor Unity Toon Shader materials
                // declare a "_BaseMap" property (so HasProperty is true) but leave it empty and
                // put the real texture in "_MainTex" instead, which a HasProperty-only priority
                // check was silently picking the wrong (empty) one of, rendering every faded
                // animal as a flat white silhouette instead of its actual texture.
                if (original.HasProperty("_MainTex")) mainTex = original.GetTexture("_MainTex");
                if (mainTex == null && original.HasProperty("_BaseMap")) mainTex = original.GetTexture("_BaseMap");

                if (original.HasProperty("_BaseColor")) tint = original.GetColor("_BaseColor");
                else if (original.HasProperty("_Color")) tint = original.GetColor("_Color");
            }

            if (mainTex != null)
            {
                if (proxy.HasProperty("_BaseMap")) proxy.SetTexture("_BaseMap", mainTex);
                else if (proxy.HasProperty("_MainTex")) proxy.SetTexture("_MainTex", mainTex);
            }

            if (proxy.HasProperty("_BaseColor")) proxy.SetColor("_BaseColor", tint);
            else proxy.color = tint;

            // Standard URP alpha-blend setup -- Universal Render Pipeline/Unlit (and the fallback
            // candidates) are stock shaders documented to support this, unlike the vendor
            // materials this proxy stands in for.
            if (proxy.HasProperty("_Surface")) proxy.SetFloat("_Surface", 1f);
            proxy.SetOverrideTag("RenderType", "Transparent");
            if (proxy.HasProperty("_SrcBlend")) proxy.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (proxy.HasProperty("_DstBlend")) proxy.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (proxy.HasProperty("_ZWrite")) proxy.SetInt("_ZWrite", 0);
            proxy.DisableKeyword("_ALPHATEST_ON");
            proxy.EnableKeyword("_ALPHABLEND_ON");
            proxy.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            proxy.renderQueue = (int)RenderQueue.Transparent;

            return proxy;
        }

        private void RestoreRenderer(Renderer renderer)
        {
            if (renderer != null && originalMaterials.TryGetValue(renderer, out var original))
            {
                renderer.sharedMaterials = original;
            }
        }
    }
}
