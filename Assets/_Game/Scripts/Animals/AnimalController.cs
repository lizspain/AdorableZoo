using System;
using System.Collections;
using RainbowZoo.Core;
using Suriyun;
using UnityEngine;
using UnityEngine.AI;

namespace RainbowZoo.Animals
{
    /// <summary>
    /// Drives one placed animal's local state machine (Idle/Wander -> Rest/Eat/Chase -> Jump ->
    /// back to Idle/Wander), composed on top of Suriyun's ControllerPetZoo (NavMeshAgent +
    /// Animator speed bridge) rather than duplicating its movement logic. ControllerPetZoo's own
    /// trigger-zone Eat/Rest detection is intentionally unused -- interactions are tap-driven
    /// (InputRouter), so this drives the "eating"/"resting" Animator bools directly instead.
    /// </summary>
    [RequireComponent(typeof(ControllerPetZoo))]
    public sealed class AnimalController : MonoBehaviour
    {
        private enum State { IdleWander, Reacting, Chasing }

        [SerializeField] private float wanderRadius = 1.6f;
        [Tooltip("Overrides the animal prefab's own NavMeshAgent.stoppingDistance, which vendor prefabs set for their original open-field demo scale (e.g. 2 units) -- far too large for our 4x4 habitat, where it made the agent consider itself 'arrived' the instant SetDestination was called, before moving at all.")]
        [SerializeField] private float stoppingDistance = 0.15f;
        [SerializeField] private float waypointArrivalThreshold = 0.15f;
        [SerializeField] private float minIdlePauseSeconds = 0.5f;
        [SerializeField] private float maxIdlePauseSeconds = 2f;
        [Tooltip("How long the Jump celebration is given to play before returning to Idle/Wander.")]
        [SerializeField] private float jumpCelebrationSeconds = 0.6f;

        private ControllerPetZoo controllerPetZoo;
        private NavMeshAgent agent;
        private AnimalDefinition definition;
        private ZooEconomyConfig economyConfig;
        private Vector3 habitatCenter;
        private float pauseTimer;
        private bool waitingAtWaypoint;
        private bool initialized;
        private State state = State.IdleWander;

        private static readonly int ParamEating = Animator.StringToHash("eating");
        private static readonly int ParamResting = Animator.StringToHash("resting");

        private void Awake()
        {
            controllerPetZoo = GetComponent<ControllerPetZoo>();
            agent = controllerPetZoo.agent;
            agent.stoppingDistance = stoppingDistance;
        }

        /// <summary>Called by ZooManager once this animal's habitat NavMesh has been baked.</summary>
        public void Initialize(Vector3 habitatWorldCenter, AnimalDefinition animalDefinition, ZooEconomyConfig config)
        {
            habitatCenter = habitatWorldCenter;
            definition = animalDefinition;
            economyConfig = config;
            initialized = true;
            PickNewWanderDestination();
        }

        private void Update()
        {
            if (!initialized || state != State.IdleWander || agent == null || !agent.isOnNavMesh) return;

            if (waitingAtWaypoint)
            {
                pauseTimer -= Time.deltaTime;
                if (pauseTimer <= 0f)
                {
                    waitingAtWaypoint = false;
                    PickNewWanderDestination();
                }
                return;
            }

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + waypointArrivalThreshold)
            {
                waitingAtWaypoint = true;
                pauseTimer = UnityEngine.Random.Range(minIdlePauseSeconds, maxIdlePauseSeconds);
            }
        }

        /// <summary>Tap on this animal's body collider. No-ops if it's mid-reaction or chasing the toy (Reacting lock, section 4).</summary>
        public bool TryPet()
        {
            if (state != State.IdleWander) return false;
            StartCoroutine(ReactSequence(ParamResting, economyConfig.PetLockSeconds, economyConfig.PetHearts));
            return true;
        }

        /// <summary>Tap on this habitat's food dish. Same lock rules as Pet.</summary>
        public bool TryFeed()
        {
            if (state != State.IdleWander) return false;
            StartCoroutine(ReactSequence(ParamEating, economyConfig.FeedLockSeconds, economyConfig.FeedHearts));
            return true;
        }

        /// <summary>Zoo-wide Care Meter completion beat (section 7) -- every placed animal plays this together, regardless of individual state.</summary>
        public void PlayCelebration()
        {
            controllerPetZoo.Jump();
        }

        private IEnumerator ReactSequence(int animatorBoolParam, float lockSeconds, int heartsEarned)
        {
            state = State.Reacting;
            controllerPetZoo.mecanim.SetBool(animatorBoolParam, true);

            yield return new WaitForSeconds(lockSeconds);

            controllerPetZoo.mecanim.SetBool(animatorBoolParam, false);
            controllerPetZoo.Jump();
            ZooManager.Instance.ReportInteractionHearts(heartsEarned);

            yield return new WaitForSeconds(jumpCelebrationSeconds);

            state = State.IdleWander;
            PickNewWanderDestination();
        }

        private void PickNewWanderDestination()
        {
            const int maxAttempts = 5;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var offset2D = UnityEngine.Random.insideUnitCircle * wanderRadius;
                var candidate = habitatCenter + new Vector3(offset2D.x, 0f, offset2D.y);

                if (NavMesh.SamplePosition(candidate, out var hit, 1f, NavMesh.AllAreas))
                {
                    controllerPetZoo.SetDestination(hit.position);
                    return;
                }
            }

            Debug.LogWarning($"[Animal] {name} all {maxAttempts} SamplePosition attempts missed the NavMesh around {habitatCenter} (radius {wanderRadius}); isOnNavMesh={agent.isOnNavMesh}. Falling back to habitat center.", this);
            controllerPetZoo.SetDestination(habitatCenter);
        }
    }
}
