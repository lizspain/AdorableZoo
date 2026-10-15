using System;
using System.Collections.Generic;
using UnityEngine;

namespace RainbowZoo.Core
{
    /// <summary>
    /// Placeholder debug VFX: a small colored particle burst (3-9 particles, fading over half a
    /// second) at an interaction's trigger point, so Pet/Feed/Play are each visually confirmable
    /// during development. These are still throwaway/replaceable once real VFX exist -- Phase 9
    /// only asked that whatever plays *today* stop paying Instantiate/Destroy on every single tap,
    /// so bursts are pooled and reused rather than the GameObject being thrown away each time.
    ///
    /// Rendered on Unity's built-in TransparentFX layer (the conventional layer for exactly this
    /// kind of effect) with a standard URP Unlit material -- respects normal depth occlusion for
    /// now. True always-on-top rendering (ignoring what's in front of it, e.g. a large animal's
    /// body) needs an Overlay Camera stacked with Clear Depth restricted to this layer; skipped
    /// for now per request, revisit once real (non-debug) VFX exist.
    /// </summary>
    public static class DebugInteractionVfx
    {
        public static readonly Color PetColor = Color.red;
        public static readonly Color FeedColor = Color.blue;
        public static readonly Color PlayColor = Color.green;

        private const float BurstLifetimeSeconds = 0.5f;
        private const float ReturnDelaySeconds = BurstLifetimeSeconds + 0.1f;

        private static Shader vfxShader;
        private static Transform poolRoot;
        private static readonly Queue<PooledBurst> pool = new Queue<PooledBurst>();

        public static void SpawnBurst(Vector3 position, Color color)
        {
            var burst = Rent();
            burst.transform.position = position;

            var ps = burst.ParticleSystem;
            var main = ps.main;
            main.startColor = color;

            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)UnityEngine.Random.Range(3, 10)) });

            ps.Clear();
            ps.Play();
            burst.ScheduleReturn(ReturnDelaySeconds);
        }

        private static PooledBurst Rent()
        {
            while (pool.Count > 0)
            {
                var candidate = pool.Dequeue();
                if (candidate == null) continue; // destroyed externally (e.g. scene unload) -- fall through and create a fresh one
                candidate.gameObject.SetActive(true);
                return candidate;
            }
            return CreateBurst();
        }

        private static void Return(PooledBurst burst)
        {
            burst.gameObject.SetActive(false);
            pool.Enqueue(burst);
        }

        private static PooledBurst CreateBurst()
        {
            if (poolRoot == null)
            {
                poolRoot = new GameObject("DebugInteractionVfxPool").transform;
            }

            var go = new GameObject("DebugInteractionBurst (pooled)");
            go.transform.SetParent(poolRoot, false);
            go.layer = LayerMask.NameToLayer("TransparentFX");

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = BurstLifetimeSeconds;
            main.startSpeed = 1.5f;
            main.startSize = 0.25f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            ApplyUrpMaterial(ps);

            var burst = go.AddComponent<PooledBurst>();
            burst.Init(ps, Return);
            return burst;
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

        /// <summary>Tiny return-to-pool timer -- the static class above has no Update/coroutine host of its own, so each pooled burst carries its own one-shot callback instead.</summary>
        private sealed class PooledBurst : MonoBehaviour
        {
            public ParticleSystem ParticleSystem { get; private set; }
            private Action<PooledBurst> onReturn;

            public void Init(ParticleSystem ps, Action<PooledBurst> returnCallback)
            {
                ParticleSystem = ps;
                onReturn = returnCallback;
            }

            public void ScheduleReturn(float delaySeconds)
            {
                CancelInvoke();
                Invoke(nameof(ReturnToPool), delaySeconds);
            }

            private void ReturnToPool() => onReturn(this);
        }
    }
}
