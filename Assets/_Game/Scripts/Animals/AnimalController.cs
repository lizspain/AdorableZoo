using System;
using System.Collections;
using System.Collections.Generic;
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
        private enum QueuedInteraction { None, Pet, Feed }

        [SerializeField] private float wanderRadius = 1.6f;
        [Tooltip("Overrides the animal prefab's own NavMeshAgent.stoppingDistance, which vendor prefabs set for their original open-field demo scale (e.g. 2 units) -- far too large for our 4x4 habitat, where it made the agent consider itself 'arrived' the instant SetDestination was called, before moving at all.")]
        [SerializeField] private float stoppingDistance = 0.15f;
        [SerializeField] private float waypointArrivalThreshold = 0.15f;
        [SerializeField] private float minIdlePauseSeconds = 0.5f;
        [SerializeField] private float maxIdlePauseSeconds = 2f;
        [Tooltip("How long the Jump celebration is given to play before returning to Idle/Wander.")]
        [SerializeField] private float jumpCelebrationSeconds = 0.6f;
        [Tooltip("How close (meters) counts as 'arrived' when chasing the toy or carrying it to the drop point.")]
        [SerializeField] private float pickupDistance = 0.5f;
        [Tooltip("Safety valve: gives up waiting to arrive at the toy or the drop point after this long, proceeding from wherever the agent actually got to, rather than risk stalling forever on an unreachable destination.")]
        [SerializeField] private float chaseTimeoutSeconds = 6f;

        private ControllerPetZoo controllerPetZoo;
        private NavMeshAgent agent;
        private AnimalDefinition definition;
        private ZooEconomyConfig economyConfig;
        private Transform runtimeAttachmentPoint;
        private Transform foodDishTransform;
        private Vector3 habitatCenter;
        private float pauseTimer;
        private bool waitingAtWaypoint;
        private bool initialized;
        private State state = State.IdleWander;
        private QueuedInteraction queuedInteraction = QueuedInteraction.None;
        private float lastPetTime = float.NegativeInfinity;
        private float lastFeedTime = float.NegativeInfinity;

        public AnimalDefinition Definition => definition;

        private static readonly int ParamEating = Animator.StringToHash("eating");
        private static readonly int ParamResting = Animator.StringToHash("resting");

        private void Awake()
        {
            controllerPetZoo = GetComponent<ControllerPetZoo>();

            // [RequireComponent(typeof(ControllerPetZoo))] silently adds a *blank* one (agent and
            // mecanim both null) if this prefab doesn't already have a properly-wired instance --
            // which happens when an AnimalDefinition's Animal Prefab points at a plain cosmetic
            // Suriyun prefab (e.g. "BearA") instead of the Agent- variant (e.g. "Agent-BearA")
            // that actually has NavMeshAgent + ControllerPetZoo configured. Left unchecked, that
            // produces a cryptic NullReferenceException spamming from vendor code every frame
            // instead of pointing at the actual mistake.
            if (controllerPetZoo.agent == null || controllerPetZoo.mecanim == null)
            {
                string agentStatus = controllerPetZoo.agent != null ? "ok" : "NULL";
                string mecanimStatus = controllerPetZoo.mecanim != null ? "ok" : "NULL";
                Debug.LogError($"[Animal] '{name}' has an unconfigured ControllerPetZoo (agent={agentStatus}, mecanim={mecanimStatus}) -- " +
                    "its AnimalDefinition's Animal Prefab is very likely a plain cosmetic Suriyun prefab rather than an Agent-* variant. " +
                    "Disabling this instance to avoid a NullReferenceException spam.", this);
                controllerPetZoo.enabled = false; // its own Update() would otherwise keep crashing on the same null agent every frame
                enabled = false;
                return;
            }

            agent = controllerPetZoo.agent;
            agent.stoppingDistance = stoppingDistance;
        }

        /// <summary>Called by ZooManager once this animal's habitat NavMesh has been baked.</summary>
        public void Initialize(Vector3 habitatWorldCenter, AnimalDefinition animalDefinition, ZooEconomyConfig config, Transform foodDish)
        {
            habitatCenter = habitatWorldCenter;
            definition = animalDefinition;
            economyConfig = config;
            foodDishTransform = foodDish;
            runtimeAttachmentPoint = ResolveRuntimeAttachmentPoint();
            initialized = true;
            PickNewWanderDestination();
        }

        /// <summary>
        /// AnimalDefinition.AttachmentPoint is a Transform on the *prefab asset*, not this
        /// instantiated animal -- Unity gives every instance its own copy of the hierarchy, so we
        /// resolve the equivalent child here by relative path rather than reusing the reference
        /// directly. Falls back to this animal's own root if unset or unresolvable, so Play still
        /// works (toy just carries at the root) before every AnimalDefinition has one authored.
        /// </summary>
        private Transform ResolveRuntimeAttachmentPoint()
        {
            if (definition.AttachmentPoint == null || definition.AnimalPrefab == null) return transform;

            var path = RelativePath(definition.AnimalPrefab.transform, definition.AttachmentPoint);
            var found = string.IsNullOrEmpty(path) ? transform : transform.Find(path);
            if (found == null)
            {
                Debug.LogWarning($"[Animal] {name}: couldn't resolve AttachmentPoint path '{path}' on the instantiated animal; falling back to the root transform for toy-carrying.", this);
                return transform;
            }
            return found;
        }

        private static string RelativePath(Transform root, Transform target)
        {
            if (target == root) return "";
            var segments = new List<string>();
            for (var current = target; current != null && current != root; current = current.parent)
            {
                segments.Insert(0, current.name);
            }
            return string.Join("/", segments);
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

        /// <summary>
        /// Tap on this animal's body collider. Rejected outright (not queued) if still within the
        /// anti-spam cooldown since the last Pet, regardless of whether the animal is otherwise
        /// free -- that cooldown is deliberately separate from how long the Rest animation plays.
        /// Otherwise: if free, reacts immediately; if mid-reaction or mid-chase, this is queued
        /// (replacing any previously queued interaction) and fires the instant the current one
        /// finishes -- no re-tap needed, and no waiting out a full extra lock on top of whatever's
        /// already in progress.
        /// </summary>
        public bool TryPet()
        {
            if (Time.time - lastPetTime < economyConfig.PetLockSeconds) return false;
            lastPetTime = Time.time;

            if (state == State.IdleWander)
            {
                BeginPet();
            }
            else
            {
                queuedInteraction = QueuedInteraction.Pet;
            }
            return true;
        }

        /// <summary>Tap on this habitat's food dish. Same cooldown-then-immediate-or-queued rule as Pet.</summary>
        public bool TryFeed()
        {
            if (Time.time - lastFeedTime < economyConfig.FeedLockSeconds) return false;
            lastFeedTime = Time.time;

            if (state == State.IdleWander)
            {
                BeginFeed();
            }
            else
            {
                queuedInteraction = QueuedInteraction.Feed;
            }
            return true;
        }

        private void BeginPet()
        {
            StartCoroutine(ReactSequence(ParamResting, economyConfig.PetAnimationSeconds, economyConfig.PetHearts));
        }

        private void BeginFeed()
        {
            StartCoroutine(FeedSequence());
        }

        /// <summary>Called whenever a reaction or chase finishes: fires whatever got queued during it, or resumes wandering if nothing did.</summary>
        private void ResumeAfterFree()
        {
            switch (queuedInteraction)
            {
                case QueuedInteraction.Pet:
                    queuedInteraction = QueuedInteraction.None;
                    BeginPet();
                    break;
                case QueuedInteraction.Feed:
                    queuedInteraction = QueuedInteraction.None;
                    BeginFeed();
                    break;
                default:
                    PickNewWanderDestination();
                    break;
            }
        }

        /// <summary>Zoo-wide Care Meter completion beat (section 7) -- every placed animal plays this together, regardless of individual state.</summary>
        public void PlayCelebration()
        {
            controllerPetZoo.Jump();
        }

        /// <summary>
        /// Play interaction, called by this habitat's own ToyController once the thrown toy has
        /// settled: chase to it, carry it to dropPoint (the habitat's Toy Drop Point), drop it,
        /// then report hearts and resume wandering. Waits for any in-progress Pet/Feed reaction to
        /// finish first rather than interrupting it, since a throw could land while this animal
        /// happens to already be mid-reaction.
        /// </summary>
        public void ChaseAndFetchToy(Transform toy, Transform dropPoint, Action onDropped)
        {
            StartCoroutine(WaitThenChase(toy, dropPoint, onDropped));
        }

        private IEnumerator WaitThenChase(Transform toy, Transform dropPoint, Action onDropped)
        {
            while (state != State.IdleWander) yield return null;
            yield return ChaseSequence(toy, dropPoint, onDropped);
        }

        private IEnumerator ChaseSequence(Transform toy, Transform dropPoint, Action onDropped)
        {
            state = State.Chasing;
            float originalSpeed = agent.speed;
            agent.speed = economyConfig.ChaseSpeed;

            // Bounded rather than an unconditional while-until-arrived: a destination that turns
            // out to be unreachable (e.g. right at a NavMesh-eroded edge, as ToyDropPoint was)
            // must never spin forever -- that stalls this animal in State.Chasing permanently,
            // which blocks Pet/Feed on it for the rest of the session. Give up and proceed from
            // wherever the agent actually got to instead.
            float elapsed = 0f;
            while (Vector3.Distance(transform.position, toy.position) > pickupDistance && elapsed < chaseTimeoutSeconds)
            {
                controllerPetZoo.SetDestination(toy.position);
                elapsed += Time.deltaTime;
                yield return null;
            }

            var toyRigidbody = toy.GetComponent<Rigidbody>();
            if (toyRigidbody != null) toyRigidbody.isKinematic = true;
            toy.SetParent(runtimeAttachmentPoint, true);
            toy.localPosition = Vector3.zero;

            elapsed = 0f;
            while (Vector3.Distance(transform.position, dropPoint.position) > pickupDistance && elapsed < chaseTimeoutSeconds)
            {
                controllerPetZoo.SetDestination(dropPoint.position);
                elapsed += Time.deltaTime;
                yield return null;
            }

            toy.SetParent(null, true);
            toy.position = dropPoint.position;
            if (toyRigidbody != null) toyRigidbody.isKinematic = false;

            agent.speed = originalSpeed;

            controllerPetZoo.Jump();
            ZooManager.Instance.ReportInteractionHearts(economyConfig.PlayHearts);
            onDropped?.Invoke();

            yield return new WaitForSeconds(jumpCelebrationSeconds);

            state = State.IdleWander;
            ResumeAfterFree();
        }

        private IEnumerator ReactSequence(int animatorBoolParam, float animationSeconds, int heartsEarned)
        {
            state = State.Reacting;
            DebugInteractionVfx.SpawnBurst(transform.position + Vector3.up * 0.5f, DebugInteractionVfx.PetColor);
            controllerPetZoo.mecanim.SetBool(animatorBoolParam, true);

            yield return new WaitForSeconds(animationSeconds);

            controllerPetZoo.mecanim.SetBool(animatorBoolParam, false);
            controllerPetZoo.Jump();
            ZooManager.Instance.ReportInteractionHearts(heartsEarned);

            yield return new WaitForSeconds(jumpCelebrationSeconds);

            state = State.IdleWander;
            ResumeAfterFree();
        }

        /// <summary>
        /// Feed: jump immediately (acknowledging the tap), walk to the food dish, play the Eat
        /// animation there, then report hearts and resume wandering -- unlike Pet, this involves
        /// real movement rather than reacting in place, so it uses its own sequence rather than
        /// sharing ReactSequence's generic bool-lock-hearts shape.
        /// </summary>
        private IEnumerator FeedSequence()
        {
            state = State.Reacting;
            controllerPetZoo.Jump();

            if (foodDishTransform != null)
            {
                float elapsed = 0f;
                while (Vector3.Distance(transform.position, foodDishTransform.position) > pickupDistance && elapsed < chaseTimeoutSeconds)
                {
                    controllerPetZoo.SetDestination(foodDishTransform.position);
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            // Clear the current path and give one frame for ControllerPetZoo.Update() to feed the
            // resulting speed=0 into the Animator before triggering Eat -- if its Idle/Eat
            // transition is gated on speed being ~0 (as Idle/Move already are), setting the bool
            // while the agent still had residual velocity mid-deceleration could mean the
            // transition's condition wasn't met yet. Deliberately not touching agent.velocity
            // directly here (tried that, suspect it's what broke Feed and Pet across the board
            // last round -- ResetPath() alone is the well-established, safe way to stop an agent).
            agent.ResetPath();
            yield return null;

            // At the dish, not the animal -- the VFX marks where the interaction is actually
            // happening (the tapped food dish), not wherever the animal started from.
            var burstPosition = foodDishTransform != null ? foodDishTransform.position : transform.position;
            DebugInteractionVfx.SpawnBurst(burstPosition + Vector3.up * 0.3f, DebugInteractionVfx.FeedColor);

            controllerPetZoo.mecanim.SetBool(ParamEating, true);
            yield return new WaitForSeconds(economyConfig.FeedAnimationSeconds);
            controllerPetZoo.mecanim.SetBool(ParamEating, false);

            ZooManager.Instance.ReportInteractionHearts(economyConfig.FeedHearts);

            state = State.IdleWander;
            ResumeAfterFree();
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
