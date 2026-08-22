using UnityEngine;

namespace RainbowZoo.Core
{
    /// <summary>
    /// Placeholder debug VFX: a small colored particle burst (3-9 particles, fading over half a
    /// second) at an interaction's trigger point, so Pet/Feed/Play are each visually confirmable
    /// during development. Deliberately not pooled -- Phase 9 handles real VFX pooling; these are
    /// throwaway, self-destructing GameObjects meant to be replaced once real VFX exist.
    ///
    /// Rendered on Unity's built-in TransparentFX layer (the conventional layer for exactly this
    /// kind of effect) with a standard URP Unlit material -- respects normal depth occlusion for
    /// now. True always-on-top rendering (ignoring what's in front of it, e.g. a large animal's
    /// body) needs an Overlay Camera stacked with Clear Depth restricted to this layer; skipped
    /// for now per request, revisit once real (non-debug) VFX exist in Phase 9 if it still matters.
    /// </summary>
    public static class DebugInteractionVfx
    {
        public static readonly Color PetColor = Color.red;
        public static readonly Color FeedColor = Color.blue;
        public static readonly Color PlayColor = Color.green;

        private static Shader vfxShader;

        public static void SpawnBurst(Vector3 position, Color color)
        {
            var go = new GameObject("DebugInteractionBurst");
            go.transform.position = position;
            go.layer = LayerMask.NameToLayer("TransparentFX");

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.5f;
            main.startSpeed = 1.5f;
            main.startSize = 0.25f;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Random.Range(3, 10)) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            ApplyUrpMaterial(ps);

            ps.Play();
            Object.Destroy(go, 0.6f);
        }

        /// <summary>
        /// Unity's default particle material isn't URP-compatible (renders magenta under this
        /// pipeline). The generic "Unlit" shader isn't the right replacement either -- it doesn't
        /// read the particle's per-instance vertex color and defaults to Opaque, so every burst
        /// rendered as a flat, fully-opaque white blob regardless of the color/fade set on it.
        /// URP's dedicated Particles/Unlit shader (what Unity's own default particle material
        /// would normally reference anyway) handles both correctly out of the box.
        /// </summary>
        private static void ApplyUrpMaterial(ParticleSystem ps)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer == null) return;

            if (vfxShader == null)
            {
                vfxShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (vfxShader == null) vfxShader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (vfxShader == null) return;

            renderer.material = new Material(vfxShader);
        }
    }
}
