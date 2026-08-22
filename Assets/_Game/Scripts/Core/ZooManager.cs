using System;
using System.Collections.Generic;
using RainbowZoo.Animals;
using Unity.AI.Navigation;
using UnityEngine;

namespace RainbowZoo.Core
{
    /// <summary>
    /// Scene singleton and sole runtime owner/writer of ZooLayoutState and ZooCareMeterState
    /// (one-way data flow rule -- see Technical Design doc). Every other system reads this
    /// state through events or exposed properties, never mutates it directly.
    /// </summary>
    public sealed class ZooManager : MonoBehaviour
    {
        public static ZooManager Instance { get; private set; }

        [SerializeField] private GameObject baseHabitatPrefab;
        [SerializeField] private ZooEconomyConfig economyConfig;
        [SerializeField] private AnimalRoster animalRoster;
        [SerializeField] private Vector3 gridOrigin = Vector3.zero;
        [Tooltip("World-space distance between adjacent plot centers. +X per column, +Z per row.")]
        [SerializeField] private float plotSpacing = 4f;

        [Header("Debug (Phase 2 placeholder placement -- coexists with the real Offer Tableau)")]
        [SerializeField] private List<AnimalDefinition> debugAnimalPool = new List<AnimalDefinition>();

        private readonly ZooLayoutState layoutState = new ZooLayoutState();
        private readonly ZooCareMeterState careMeterState = new ZooCareMeterState();
        private readonly List<GameObject> instantiatedHabitats = new List<GameObject>();
        private OfferGenerator offerGenerator;
        private int debugPoolCursor;

        public ZooLayoutState LayoutState => layoutState;
        public ZooCareMeterState CareMeterState => careMeterState;

        /// <summary>Raised whenever a new 3-slot offer is ready to display (Offer Tableau UI, section 3).</summary>
        public event Action<OfferTableau> OnTableauReady;

        /// <summary>Raised the moment the shared Care Meter fills (section 6/7).</summary>
        public event Action OnCareMeterComplete;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("Multiple ZooManager instances in scene; destroying the duplicate.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            if (economyConfig != null)
            {
                careMeterState.StartNextCycle(economyConfig.Threshold(1));
            }

            if (animalRoster != null && economyConfig != null)
            {
                offerGenerator = new OfferGenerator(animalRoster, economyConfig);
                RequestNextTableau();
            }
            else
            {
                Debug.LogWarning("animalRoster or economyConfig unassigned -- Offer Tableau will not be requested.", this);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                PlaceNextDebugAnimal();
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                RequestNextTableau();
            }
        }

        /// <summary>Instantiates definition's habitat at the next plot and records it in ZooLayoutState.</summary>
        public GameObject PlaceAnimal(AnimalDefinition definition)
        {
            var plot = layoutState.NextPlotCoordinate();
            var prefab = definition.HabitatPrefabOverride != null ? definition.HabitatPrefabOverride : baseHabitatPrefab;

            if (prefab == null)
            {
                Debug.LogError($"No habitat prefab available for '{definition.Id}' (no override, and baseHabitatPrefab is unassigned).", this);
                return null;
            }

            var worldPosition = PlotToWorld(plot);
            var habitat = Instantiate(prefab, worldPosition, Quaternion.identity, transform);
            habitat.name = $"Habitat_{definition.Id}_{plot}";
            instantiatedHabitats.Add(habitat);

            BakeHabitatNavMesh(habitat);
            SpawnAnimal(definition, habitat, worldPosition);

            layoutState.PlaceNext(definition.Id);

            // The tableau hides itself on tap (OfferTableauController) and only reappears once
            // the shared Care Meter fills (ReportInteractionHearts) -- T remains as a debug
            // shortcut for testing without needing to actually fill the meter first.

            return habitat;
        }

        public Vector3 PlotToWorld(PlotCoordinate plot)
        {
            return gridOrigin + new Vector3((plot.Column - 1) * plotSpacing, 0f, (plot.Row - 1) * plotSpacing);
        }

        /// <summary>
        /// Synchronous bake, scoped to just this habitat instance's own children (Floor, plus
        /// any decoration prop) via NavMeshSurface.collectObjects = Children. Synchronous rather
        /// than async for now -- correctness over micro-optimization at this stage, since an
        /// animal spawning before its habitat's NavMesh exists would fail to path. Phase 9
        /// revisits async if profiling shows the per-placement cost actually matters.
        /// </summary>
        private void BakeHabitatNavMesh(GameObject habitat)
        {
            var surface = habitat.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                Debug.LogError($"Habitat prefab '{habitat.name}' has no NavMeshSurface -- was it created before the base habitat prefab was regenerated?", habitat);
                return;
            }
            surface.BuildNavMesh();
        }

        private void SpawnAnimal(AnimalDefinition definition, GameObject habitat, Vector3 habitatCenter)
        {
            if (definition.AnimalPrefab == null)
            {
                Debug.LogError($"AnimalDefinition '{definition.Id}' has no Animal Prefab assigned.", this);
                return;
            }

            var animal = Instantiate(definition.AnimalPrefab, habitatCenter, Quaternion.identity, habitat.transform);
            animal.name = definition.Id;

            var controller = animal.GetComponent<AnimalController>();
            if (controller == null)
            {
                controller = animal.AddComponent<AnimalController>();
            }

            // HabitatRuntime resolves ToyDropPoint/FoodDish before AnimalController.Initialize
            // needs FoodDish (Feed now walks the animal there rather than reacting in place).
            var habitatRuntime = habitat.AddComponent<HabitatRuntime>();
            habitatRuntime.Initialize(controller);

            controller.Initialize(habitatCenter, definition, economyConfig, habitatRuntime.FoodDish);

            // One Toy per habitat (not shared zoo-wide) -- Play on one habitat never blocks or
            // steals from Play on another.
            habitat.AddComponent<ToyController>();
        }

        /// <summary>
        /// AnimalController calls this after a completed Pet/Play/Feed interaction (sole writer
        /// of ZooCareMeterState). On threshold, triggers the zoo-wide Celebration, starts the
        /// next cycle, and requests the next Offer Tableau -- the real trigger for that request,
        /// replacing the Space/T debug shortcuts used to test it in earlier phases.
        /// </summary>
        public void ReportInteractionHearts(int hearts)
        {
            careMeterState.AddHearts(hearts);
            Debug.Log($"[CareMeter] {careMeterState.currentHearts}/{careMeterState.currentThreshold}");

            if (!careMeterState.IsComplete) return;

            OnCareMeterComplete?.Invoke();
            foreach (var habitat in instantiatedHabitats)
            {
                var runtime = habitat.GetComponent<HabitatRuntime>();
                runtime?.Animal?.PlayCelebration();
            }

            careMeterState.StartNextCycle(economyConfig.Threshold(layoutState.Count + 1));
            RequestNextTableau();
        }

        /// <summary>Last tableau generated -- lets a UI that subscribes late (GameObject/script
        /// init order between separate objects isn't guaranteed) catch up immediately instead
        /// of waiting for the next placement.</summary>
        public OfferTableau CurrentTableau { get; private set; }

        private void RequestNextTableau()
        {
            if (offerGenerator == null) return;
            CurrentTableau = offerGenerator.GenerateOffer(layoutState);
            OnTableauReady?.Invoke(CurrentTableau);
        }

        [ContextMenu("Debug: Place Next Animal")]
        private void PlaceNextDebugAnimal()
        {
            if (debugAnimalPool.Count == 0)
            {
                Debug.LogWarning("debugAnimalPool is empty -- assign placeholder AnimalDefinition assets in the Inspector.", this);
                return;
            }

            var definition = debugAnimalPool[debugPoolCursor % debugAnimalPool.Count];
            debugPoolCursor++;
            PlaceAnimal(definition);
        }
    }
}
