using System.Collections;
using RainbowZoo.Animals;
using UnityEngine;

namespace RainbowZoo.Core
{
    /// <summary>
    /// This habitat's own Toy. Each habitat gets its own instance (reversed from the original
    /// "one toy for the whole zoo" design once that turned out to make Play interactions on
    /// different habitats contend with each other) -- Play on one habitat never blocks or steals
    /// from Play on another. Rigidbody-driven, re-skinned per species. Owns its full lifecycle:
    /// follows the touch while held, gets thrown on release, waits for it to settle, hands off to
    /// this habitat's AnimalController to chase/carry/drop it, then despawns a few seconds after
    /// being dropped.
    /// </summary>
    [RequireComponent(typeof(HabitatRuntime))]
    public sealed class ToyController : MonoBehaviour
    {
        [SerializeField] private float dropVisibleSeconds = 3f;
        [SerializeField] private float settleSpeedThreshold = 0.05f;
        [SerializeField] private float settleTimeoutSeconds = 5f;

        private HabitatRuntime habitatRuntime;
        private GameObject toy;
        private Rigidbody toyRigidbody;
        private MeshFilter toyMeshFilter;
        private MeshRenderer toyMeshRenderer;

        public bool IsBusy { get; private set; }

        private void Awake()
        {
            habitatRuntime = GetComponent<HabitatRuntime>();
            BuildToy();
        }

        private void BuildToy()
        {
            toy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            toy.name = "Toy";
            toy.transform.SetParent(transform, false);
            toy.transform.localScale = Vector3.one * 0.3f;

            toyMeshFilter = toy.GetComponent<MeshFilter>();
            toyMeshRenderer = toy.GetComponent<MeshRenderer>();
            toyRigidbody = toy.AddComponent<Rigidbody>();
            toy.SetActive(false);
        }

        /// <summary>Begins a Play hold: activates and re-skins the toy, positions it at worldPoint, and holds it kinematic until Release.</summary>
        public void BeginHold(Vector3 worldPoint)
        {
            if (IsBusy) return;
            IsBusy = true;

            ApplyAppearance(habitatRuntime.Animal.Definition.ToyAppearance);

            toy.transform.SetParent(transform, false);
            toy.transform.position = worldPoint;
            toyRigidbody.isKinematic = true;
            toy.SetActive(true);
        }

        /// <summary>Called every frame while held, to follow the touch.</summary>
        public void UpdateHoldPosition(Vector3 worldPoint)
        {
            if (!IsBusy) return;
            toy.transform.position = worldPoint;
        }

        /// <summary>Throws the toy with the given velocity, then waits for it to settle before handing off to this habitat's animal.</summary>
        public void Release(Vector3 throwVelocity)
        {
            if (!IsBusy) return;
            toyRigidbody.isKinematic = false;
            toyRigidbody.linearVelocity = throwVelocity;
            DebugInteractionVfx.SpawnBurst(toy.transform.position, Color.green);
            StartCoroutine(WaitForSettleThenHandoff());
        }

        private IEnumerator WaitForSettleThenHandoff()
        {
            float elapsed = 0f;
            while (elapsed < settleTimeoutSeconds)
            {
                if (toyRigidbody.linearVelocity.sqrMagnitude < settleSpeedThreshold * settleSpeedThreshold)
                {
                    break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            habitatRuntime.Animal.ChaseAndFetchToy(toy.transform, habitatRuntime.ToyDropPoint, OnDropped);
        }

        private void OnDropped()
        {
            StartCoroutine(DespawnAfterDelay());
        }

        private IEnumerator DespawnAfterDelay()
        {
            yield return new WaitForSeconds(dropVisibleSeconds);
            toy.SetActive(false);
            toy.transform.SetParent(transform, false);
            IsBusy = false;
        }

        private void ApplyAppearance(ToyAppearance appearance)
        {
            if (appearance.mesh != null) toyMeshFilter.mesh = appearance.mesh;
            if (appearance.materials != null && appearance.materials.Length > 0) toyMeshRenderer.materials = appearance.materials;
        }
    }
}
