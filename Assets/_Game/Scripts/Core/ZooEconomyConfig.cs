using System;
using UnityEngine;

namespace RainbowZoo.Core
{
    [CreateAssetMenu(menuName = "Rainbow Zoo/Zoo Economy Config", fileName = "ZooEconomyConfig")]
    public sealed class ZooEconomyConfig : ScriptableObject
    {
        [Header("Heart Values")]
        [SerializeField] private int petHearts = 1;
        [SerializeField] private int playHearts = 2;
        [SerializeField] private int feedHearts = 1;

        [Header("Care Meter Threshold: round(Base + Growth*(n-1) + Accel*(n-1)^2)")]
        [SerializeField] private float thresholdBase = 10.5f;
        [SerializeField] private float thresholdGrowthPerAnimal = 1.4f;
        [SerializeField] private float thresholdAccelPerAnimal = 0.6f;

        [Header("Offer Tableau")]
        [Range(0f, 1f)]
        [SerializeField] private float mythicalProbability = 0.05f;
        [Tooltip("Selection weight multiplier applied to animals the player already owns (doc: half weight).")]
        [Range(0f, 1f)]
        [SerializeField] private float ownedAnimalWeightMultiplier = 0.5f;

        [Header("Movement")]
        [Tooltip("Shared Move blend-tree speed value used for every species' Chase state (Play interaction). No per-species run tier.")]
        [SerializeField] private float chaseSpeed = 4f;

        [Header("Interaction Locks")]
        [Tooltip("Seconds an animal's Pet reaction plays before it's free again. A different interaction requested during this window is queued and fires immediately once it ends, rather than being dropped.")]
        [SerializeField] private float petLockSeconds = 0.5f;
        [Tooltip("Seconds an animal's Feed reaction plays before it's free again. Same queuing rule as Pet.")]
        [SerializeField] private float feedLockSeconds = 0.5f;

        [Header("Shared Toy")]
        [Tooltip("Seconds the shared Toy remains visible at the habitat's Toy Drop Point before despawning back to the pool.")]
        [SerializeField] private float toyDropDurationSeconds = 3f;

        public int PetHearts => petHearts;
        public int PlayHearts => playHearts;
        public int FeedHearts => feedHearts;
        public float MythicalProbability => mythicalProbability;
        public float OwnedAnimalWeightMultiplier => ownedAnimalWeightMultiplier;
        public float ChaseSpeed => chaseSpeed;
        public float PetLockSeconds => petLockSeconds;
        public float FeedLockSeconds => feedLockSeconds;
        public float ToyDropDurationSeconds => toyDropDurationSeconds;

        /// <summary>
        /// Hearts needed to unlock animal (animalsOwned + 1). Round-half-to-even, per the design doc
        /// (10.5 -> 10, 32.5 -> 32), matching System.Math.Round's default MidpointRounding.ToEven.
        /// </summary>
        public int Threshold(int animalsOwned)
        {
            if (animalsOwned < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(animalsOwned), animalsOwned, "Zoo must have at least 1 animal to compute a threshold.");
            }

            double n = animalsOwned - 1;
            double value = thresholdBase + thresholdGrowthPerAnimal * n + thresholdAccelPerAnimal * n * n;

            // thresholdGrowthPerAnimal/thresholdAccelPerAnimal are floats (Inspector-friendly),
            // and values like 1.4f/0.6f aren't exactly representable in binary -- multiplying
            // them by n accumulates enough error that an intended exact ".5" (e.g. 32.5) can land
            // a few millionths off, which silently breaks the round-half-to-even tie-break below
            // (e.g. 32.5000005 rounds to 33, not 32). Snapping to 4 decimal places first is far
            // more precision than an integer heart count needs, and restores the exact tie.
            value = Math.Round(value, 4, MidpointRounding.AwayFromZero);
            return (int)Math.Round(value, MidpointRounding.ToEven);
        }
    }
}
