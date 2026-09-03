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
        [Tooltip("Multiplies the animal prefab's own NavMeshAgent.speed -- the vendor's baked-in ambient wander speed, tuned for their original open-field demo scale -- so ordinary wandering reads as casual/relaxed rather than brisk. Went 0.7 -> 0.45 -> 0.25 across three rounds of \"tune it down even more.\" Chase/Feed-approach speed (ZooEconomyConfig.ChaseSpeed, applied and restored around agent.speed elsewhere) is a separate absolute value and unaffected by this.")]
        [SerializeField] private float wanderSpeedMultiplier = 0.25f;
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
        [Tooltip("Minimum time after a fresh throw before the chase is allowed to consider the toy 'caught,' even if the animal is already standing within pickupDistance of it. A 4x4 habitat is small enough that the animal can otherwise reach a just-thrown toy in a couple of frames, catching it before its bounce/flight (ToyController's PhysicMaterial) is ever actually visible -- this just makes the chase keep tracking it for a beat first.")]
        [SerializeField] private float minToyFlightSecondsBeforePickup = 0.5f;
        [Tooltip("How many random legs the animal runs around the habitat with the toy (still at ChaseSpeed) after picking it up, before heading to the Toy Drop Point -- purely a visual flourish so Play doesn't read as an instant pickup-and-drop.")]
        [SerializeField] private int playCarryAroundLegs = 2;
        [Tooltip("Safety valve per carry-around leg -- same reasoning as chaseTimeoutSeconds.")]
        [SerializeField] private float playCarryAroundLegTimeoutSeconds = 1.5f;

        private ControllerPetZoo controllerPetZoo;
        private NavMeshAgent agent;
        private AudioSource audioSource;
        private string lastAnimatorState;
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
        private bool suppressNextJumpSfx;
        private bool isSimplified;

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
            agent.speed *= wanderSpeedMultiplier;

            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
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
            if (!isSimplified)
            {
                PollAnimatorAudio(); // always runs, independent of our own state machine below
            }

            if (!initialized || state != State.IdleWander || agent == null || !agent.isOnNavMesh || isSimplified) return;

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
        /// Audio Architecture (design doc section 9): SFX are fired from Animator state
        /// transitions, never directly from raw input. The vendor Animator Controllers can't be
        /// edited here to add Animation Events/StateMachineBehaviours, so this polls
        /// ControllerPetZoo's own friendly state name each frame and fires exactly once per
        /// transition -- the same sync guarantee (audio can't drift from what's animating),
        /// detected rather than authored. "Move" only triggers Play's SFX while we're actually
        /// Chasing (not during ordinary ambient wandering, which is also a "Move" state).
        /// </summary>
        private void PollAnimatorAudio()
        {
            if (controllerPetZoo == null || definition == null || AudioDirector.Instance == null) return;

            string current = controllerPetZoo.GetCurrentState();
            if (current == lastAnimatorState) return;
            lastAnimatorState = current;

            switch (current)
            {
                case "Rest":
                    AudioDirector.Instance.PlaySfx(audioSource, definition.PetSfx);
                    break;
                case "Eat":
                    AudioDirector.Instance.PlaySfx(audioSource, definition.FeedSfx);
                    break;
                case "Move":
                    if (state == State.Chasing)
                    {
                        AudioDirector.Instance.PlaySfx(audioSource, definition.PlaySfx);
                    }
                    break;
                case "Jump":
                    // The zoo-wide Care Meter completion beat (PlayCelebration, called on every
                    // placed animal at once) also drives this same "Jump" state but must stay
                    // silent here -- that moment gets its own tableau-appear SFX instead (see
                    // AudioDirector.PlayTableauFanfare / OfferTableauController), not this
                    // per-animal clip layered N times over each other.
                    if (suppressNextJumpSfx)
                    {
                        suppressNextJumpSfx = false;
                    }
                    else
                    {
                        AudioDirector.Instance.PlaySfx(audioSource, definition.CelebrationSfx);
                    }
                    break;
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

        /// <summary>Zoo-wide Care Meter completion beat (section 7) -- every placed animal plays this together,
        /// regardless of individual state. Visual only: the audio beat for this moment is the tableau's own
        /// fanfare (AudioDirector.PlayTableauFanfare), not this animal's CelebrationSfx.</summary>
        public void PlayCelebration()
        {
            suppressNextJumpSfx = true;
            controllerPetZoo.Jump();
        }

        /// <summary>
        /// Off-camera simplification (design doc section 13, Phase 9): pauses wander/audio-polling
        /// while this habitat is outside the camera's frustum (HabitatVisibilityLod), and stops the
        /// NavMeshAgent outright rather than leaving it to keep steering toward a destination no one
        /// can see. Only touches idle wandering -- an animal can't be mid-interaction (Reacting/
        /// Chasing) while off-camera, since the player can't tap something they can't see, so those
        /// coroutines are left alone regardless of this flag. Resuming visibility picks a fresh
        /// wander destination rather than resuming a possibly long-stale path.
        /// </summary>
        public void SetSimplified(bool simplified)
        {
            if (isSimplified == simplified) return;
            isSimplified = simplified;

            if (agent == null || !agent.isOnNavMesh) return;

            if (isSimplified)
            {
                agent.isStopped = true;
            }
            else if (state == State.IdleWander)
            {
                agent.isStopped = false;
                waitingAtWaypoint = false; // otherwise Update() ignores the fresh destination below and counts down a stale pause timer from before this animal went off-camera
                PickNewWanderDestination();
            }
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
            //
            // Also gated on minToyFlightSecondsBeforePickup regardless of distance -- otherwise an
            // animal already standing near the throw point catches a just-thrown toy almost
            // instantly, before its bounce is ever visible.
            float elapsed = 0f;
            while ((elapsed < minToyFlightSecondsBeforePickup || Vector3.Distance(transform.position, toy.position) > pickupDistance) && elapsed < chaseTimeoutSeconds)
            {
                controllerPetZoo.SetDestination(toy.position);
                elapsed += Time.deltaTime;
                yield return null;
            }

            var toyRigidbody = toy.GetComponent<Rigidbody>();
            if (toyRigidbody != null) toyRigidbody.isKinematic = true;
            toy.SetParent(runtimeAttachmentPoint, true);
            // Not just Vector3.zero -- a bone's own pivot isn't always where a toy should
            // visually rest (e.g. a head bone's pivot can sit at the top of the skull rather than
            // near the mouth). ToyAttachmentOffset/RotationOffset are tuned per species via
            // Rainbow Zoo > Content > Toy Attachment Preview.
            toy.localPosition = definition.ToyAttachmentOffset;
            toy.localRotation = Quaternion.Euler(definition.ToyAttachmentRotationOffset);

            yield return CarryAroundHabitat();

            elapsed = 0f;
            while (Vector3.Distance(transform.position, dropPoint.position) > pickupDistance && elapsed < chaseTimeoutSeconds)
            {
                controllerPetZoo.SetDestination(dropPoint.position);
                elapsed += Time.deltaTime;
                yield return null;
            }

            // worldPositionStays:true keeps the toy exactly where it was carried (the attach
            // point, e.g. mouth height) rather than snapping it to dropPoint -- there's nothing
            // left to make it fall to the ground if it's already teleported there. Un-kinematic
            // afterward hands it to physics from that carried position, so it drops naturally
            // under gravity instead of popping straight to the floor.
            toy.SetParent(null, true);
            if (toyRigidbody != null) toyRigidbody.isKinematic = false;

            agent.speed = originalSpeed;

            controllerPetZoo.Jump();
            DebugInteractionVfx.SpawnBurst(dropPoint.position + Vector3.up * 0.3f, DebugInteractionVfx.CareColor);
            ZooManager.Instance.ReportInteractionHearts(economyConfig.PlayHearts);
            onDropped?.Invoke();

            yield return new WaitForSeconds(jumpCelebrationSeconds);

            state = State.IdleWander;
            ResumeAfterFree();
        }

        /// <summary>Runs a few random legs around the habitat (still carrying the toy, still at ChaseSpeed) between pickup and heading to the Toy Drop Point -- otherwise Play reads as an instant grab-and-drop with no sense of actually playing.</summary>
        private IEnumerator CarryAroundHabitat()
        {
            for (int leg = 0; leg < playCarryAroundLegs; leg++)
            {
                var offset2D = UnityEngine.Random.insideUnitCircle * wanderRadius;
                var candidate = habitatCenter + new Vector3(offset2D.x, 0f, offset2D.y);
                if (!NavMesh.SamplePosition(candidate, out var hit, 1f, NavMesh.AllAreas)) continue;

                float legElapsed = 0f;
                while (Vector3.Distance(transform.position, hit.position) > waypointArrivalThreshold && legElapsed < playCarryAroundLegTimeoutSeconds)
                {
                    controllerPetZoo.SetDestination(hit.position);
                    legElapsed += Time.deltaTime;
                    yield return null;
                }
            }
        }

        private IEnumerator ReactSequence(int animatorBoolParam, float animationSeconds, int heartsEarned)
        {
            state = State.Reacting;
            DebugInteractionVfx.SpawnBurst(transform.position + Vector3.up * 0.5f, DebugInteractionVfx.PetColor);
            controllerPetZoo.mecanim.SetBool(animatorBoolParam, true);

            yield return new WaitForSeconds(animationSeconds);

            controllerPetZoo.mecanim.SetBool(animatorBoolParam, false);
            controllerPetZoo.Jump();
            DebugInteractionVfx.SpawnBurst(transform.position + Vector3.up * 0.5f, DebugInteractionVfx.CareColor);
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
                // Boosted the same as Play's approach to the toy -- previously this walked at
                // ordinary wander speed, so a dish tap while the animal was on the far side of
                // the habitat read as a long, unexplained pause before Eat/VFX ever fired.
                float originalSpeed = agent.speed;
                agent.speed = economyConfig.ChaseSpeed;

                float elapsed = 0f;
                while (Vector3.Distance(transform.position, foodDishTransform.position) > pickupDistance && elapsed < chaseTimeoutSeconds)
                {
                    controllerPetZoo.SetDestination(foodDishTransform.position);
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                agent.speed = originalSpeed;
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

            DebugInteractionVfx.SpawnBurst(burstPosition + Vector3.up * 0.3f, DebugInteractionVfx.CareColor);
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
