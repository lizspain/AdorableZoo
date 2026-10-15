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
    ///
    /// Also owns the placeholder heart-gain VFX: a small yellow spark spawned at the fill bar's
    /// OWN new leading edge (not at the interacting animal, per feedback that a world-space burst
    /// read as detached from the UI it's meant to call attention to) each time hearts genuinely
    /// increase -- detected by comparing against the previously-seen heart count, since
    /// OnCareHeartsChanged also fires (with a lower value) right after a completing increment
    /// resets to the next cycle, which is a drop, not a gain, and shouldn't spark.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CareMeterUIController : MonoBehaviour
    {
        [Tooltip("How long the placeholder heart-gain spark takes to fade out.")]
        [SerializeField] private float sparkFadeSeconds = 0.45f;
        [Tooltip("Spark diameter in pixels.")]
        [SerializeField] private float sparkSize = 26f;

        private UIDocument document;
        private VisualElement root;
        private VisualElement track;
        private VisualElement fill;
        private VisualElement stain;
        private int lastKnownHearts = -1;

        private void Awake()
        {
            document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            root = document.rootVisualElement;
            track = root.Q<VisualElement>("care-meter-track");
            fill = root.Q<VisualElement>("care-meter-fill");
            stain = root.Q<VisualElement>("care-meter-stain");

            // OnEnable can run again after a disable/enable cycle on the same document -- only
            // add the marker once.
            if (fill != null && fill.Q<WoodenHeart>() == null)
            {
                var heart = new WoodenHeart(stain);
                heart.AddToClassList("care-meter-heart");
                fill.Add(heart);
            }
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

            // lastKnownHearts starts at -1 specifically so the very first call (the Start()
            // catch-up read of a possibly-already-partial restored save) never sparks -- only a
            // genuine increase from an actually-observed previous value should.
            if (lastKnownHearts >= 0 && currentHearts > lastKnownHearts)
            {
                SpawnCareSpark(fraction);
            }
            lastKnownHearts = currentHearts;

            fill.style.width = new StyleLength(new Length(fraction * 100f, LengthUnit.Percent));
        }

        /// <summary>Spawns a self-removing yellow spark, in root's (screen) coordinate space, at the track's new fill-edge position -- track.worldBound is already in that same space since root spans the whole panel from (0,0).</summary>
        private void SpawnCareSpark(float fraction)
        {
            if (root == null || track == null) return;

            var bound = track.worldBound;
            if (bound.width <= 0f) return; // not laid out yet -- shouldn't happen by the time hearts actually change, but cheap to guard

            var spark = new VisualElement();
            spark.AddToClassList("care-meter-spark");
            spark.style.width = sparkSize;
            spark.style.height = sparkSize;
            // Percentage border-radius support varies by Unity version -- setting it here in
            // pixels alongside the size it's actually derived from is unambiguous either way.
            spark.style.borderTopLeftRadius = sparkSize * 0.5f;
            spark.style.borderTopRightRadius = sparkSize * 0.5f;
            spark.style.borderBottomLeftRadius = sparkSize * 0.5f;
            spark.style.borderBottomRightRadius = sparkSize * 0.5f;
            spark.style.marginLeft = -sparkSize * 0.5f;
            spark.style.marginTop = -sparkSize * 0.5f;
            spark.style.left = bound.x + bound.width * fraction;
            spark.style.top = bound.y + bound.height * 0.5f;
            root.Add(spark);

            spark.experimental.animation
                .Start(1f, 0f, Mathf.RoundToInt(sparkFadeSeconds * 1000f), (element, value) => element.style.opacity = value)
                .OnCompleted(() => spark.RemoveFromHierarchy());
        }

        /// <summary>
        /// The wooden heart peg marking the fill's leading edge. USS has no clip-path, so the
        /// heart is drawn as generated mesh in three stacked layers that echo the dropdown peg in
        /// the Figma Make kit: a dark side wall offset below, a lighter chamfer rim, and an inset
        /// face. Each layer is tinted and textured with the birch grain taken from grainSource's
        /// resolved USS background (the cherry stain, which uses the contrast-boosted grain map),
        /// so the wood grain stays defined in one place -- the stylesheet.
        /// </summary>
        private sealed class WoodenHeart : VisualElement
        {
            private const int Segments = 40;
            private const float WallDepth = 3f;
            private const float GrainUvPerPixel = 1f / 1024f;
            private const float FaceInset = 0.72f;

            private static readonly Color32 WallTint = new Color32(0x54, 0x10, 0x10, 255);
            private static readonly Color32 RimTint = new Color32(0x96, 0x30, 0x30, 255);
            private static readonly Color32 FaceTint = new Color32(0x7F, 0x1D, 0x1D, 255);

            // Heart outline normalized to [-1, 1] on both axes, y-down, traced clockwise on screen.
            private static readonly Vector2[] UnitOutline = BuildUnitOutline();

            private readonly VisualElement grainSource;

            public WoodenHeart(VisualElement grainSource)
            {
                this.grainSource = grainSource;
                pickingMode = PickingMode.Ignore;
                generateVisualContent += OnGenerateVisualContent;
            }

            private static Vector2[] BuildUnitOutline()
            {
                // Classic parametric heart: x = 16 sin^3 t, y = 13 cos t - 5 cos 2t - 2 cos 3t - cos 4t.
                // Its bounding box isn't centered on the origin, so normalize by the actual extents.
                var raw = new Vector2[Segments];
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                for (int i = 0; i < Segments; i++)
                {
                    float t = i / (float)Segments * 2f * Mathf.PI;
                    float s = Mathf.Sin(t);
                    float x = 16f * s * s * s;
                    float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                    raw[i] = new Vector2(x, -y); // -y: screen space is y-down
                    min = Vector2.Min(min, raw[i]);
                    max = Vector2.Max(max, raw[i]);
                }

                var center = (min + max) * 0.5f;
                var half = (max - min) * 0.5f;
                var outline = new Vector2[Segments];
                for (int i = 0; i < Segments; i++)
                {
                    outline[i] = new Vector2((raw[i].x - center.x) / half.x, (raw[i].y - center.y) / half.y);
                }
                return outline;
            }

            private void OnGenerateVisualContent(MeshGenerationContext mgc)
            {
                var rect = contentRect;
                if (rect.width <= 0f || rect.height <= WallDepth) return;

                // Null if the stylesheet hasn't resolved yet -- the layers then draw as flat tints.
                Texture grain = grainSource != null ? grainSource.resolvedStyle.backgroundImage.texture : null;

                float bodyHeight = rect.height - WallDepth;
                var center = new Vector2(rect.x + rect.width * 0.5f, rect.y + bodyHeight * 0.5f);
                var half = new Vector2(rect.width * 0.5f, bodyHeight * 0.5f);

                AddLayer(mgc, grain, center + new Vector2(0f, WallDepth), half, WallTint);
                AddLayer(mgc, grain, center, half, RimTint);
                AddLayer(mgc, grain, center + new Vector2(0f, -0.5f), half * FaceInset, FaceTint);
            }

            /// <summary>One heart-shaped triangle fan. The heart is star-shaped about its center, so a simple fan from the middle covers it exactly.</summary>
            private static void AddLayer(MeshGenerationContext mgc, Texture grain, Vector2 center, Vector2 half, Color32 tint)
            {
                var mesh = mgc.Allocate(Segments + 1, Segments * 3, grain);
                var region = mesh.uvRegion;

                var vertices = new Vertex[Segments + 1];
                vertices[0] = MakeVertex(center, region, tint);
                for (int i = 0; i < Segments; i++)
                {
                    var position = center + Vector2.Scale(UnitOutline[i], half);
                    vertices[i + 1] = MakeVertex(position, region, tint);
                }

                var indices = new ushort[Segments * 3];
                for (int i = 0; i < Segments; i++)
                {
                    indices[i * 3] = 0;
                    indices[i * 3 + 1] = (ushort)(1 + i);
                    indices[i * 3 + 2] = (ushort)(1 + (i + 1) % Segments);
                }

                mesh.SetAllVertices(vertices);
                mesh.SetAllIndices(indices);
            }

            private static Vertex MakeVertex(Vector2 position, Rect uvRegion, Color32 tint)
            {
                // Grain UVs follow position so all three layers sample the same wood; they stay
                // well inside [0,1] for a peg this small. uvRegion remaps into the texture's slot
                // if UI Toolkit has packed it into an atlas.
                var uv = position * GrainUvPerPixel;
                return new Vertex
                {
                    position = new Vector3(position.x, position.y, Vertex.nearZ),
                    tint = tint,
                    uv = new Vector2(uvRegion.xMin + uv.x * uvRegion.width, uvRegion.yMin + uv.y * uvRegion.height)
                };
            }
        }
    }
}
