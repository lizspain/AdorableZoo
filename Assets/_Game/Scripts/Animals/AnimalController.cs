using Suriyun;
using UnityEngine;
using UnityEngine.AI;

namespace RainbowZoo.Animals
{
    /// <summary>
    /// Drives the Idle/Wander state for one placed animal, composed on top of Suriyun's
    /// ControllerPetZoo (NavMeshAgent + Animator speed bridge, section 4/Technical Design)
    /// rather than duplicating its movement logic. ControllerPetZoo's own trigger-zone Eat/Rest
    /// detection is intentionally unused -- interactions are tap-driven (Phase 5 wires those
    /// Animator bools directly instead).
    /// </summary>
    [RequireComponent(typeof(ControllerPetZoo))]
    public sealed class AnimalController : MonoBehaviour
    {
        [SerializeField] private float wanderRadius = 1.6f;
        [Tooltip("Overrides the animal prefab's own NavMeshAgent.stoppingDistance, which vendor prefabs set for their original open-field demo scale (e.g. 2 units) -- far too large for our 4x4 habitat, where it made the agent consider itself 'arrived' the instant SetDestination was called, before moving at all.")]
        [SerializeField] private float stoppingDistance = 0.15f;
        [SerializeField] private float waypointArrivalThreshold = 0.15f;
        [SerializeField] private float minIdlePauseSeconds = 0.5f;
        [SerializeField] private float maxIdlePauseSeconds = 2f;

        private ControllerPetZoo controllerPetZoo;
        private NavMeshAgent agent;
        private Vector3 habitatCenter;
        private float pauseTimer;
        private bool waitingAtWaypoint;
        private bool initialized;

        private void Awake()
        {
            controllerPetZoo = GetComponent<ControllerPetZoo>();
            agent = controllerPetZoo.agent;
            agent.stoppingDistance = stoppingDistance;
        }

        /// <summary>Called by whatever spawns this animal (ZooManager), once its habitat's NavMesh has been baked.</summary>
        public void Initialize(Vector3 habitatWorldCenter)
        {
            habitatCenter = habitatWorldCenter;
            initialized = true;
            PickNewWanderDestination();
        }

        private void Update()
        {
            if (!initialized || agent == null || !agent.isOnNavMesh) return;

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
                pauseTimer = Random.Range(minIdlePauseSeconds, maxIdlePauseSeconds);
            }
        }

        private void PickNewWanderDestination()
        {
            const int maxAttempts = 5;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var offset2D = Random.insideUnitCircle * wanderRadius;
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
