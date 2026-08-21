using UnityEngine;

namespace RainbowZoo.Core
{
    /// <summary>
    /// Placeholder debug VFX: a small colored particle burst (3-9 particles, fading over half a
    /// second) at an interaction's trigger point, so Pet/Feed/Play are each visually confirmable
    /// during development. Deliberately not pooled -- Phase 9 handles real VFX pooling; these are
    /// throwaway, self-destructing GameObjects meant to be replaced once real VFX exist.
    /// </summary>
    public static class DebugInteractionVfx
    {
        public static readonly Color PetColor = Color.red;
        public static readonly Color FeedColor = Color.blue;
        public static readonly Color PlayColor = Color.green;

        public static void SpawnBurst(Vector3 position, Color color)
        {
            var go = new GameObject("DebugInteractionBurst");
            go.transform.position = position;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.5f;
            main.startSpeed = 1.5f;
            main.startSize = 0.15f;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Random.Range(3, 10)) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            ps.Play();
            Object.Destroy(go, 0.6f);
        }
    }
}
