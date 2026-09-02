using System;
using System.Collections.Generic;
using RainbowZoo.Animals;
using RainbowZoo.Save;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private bool isRestoringFromSave;
        private bool hasFullUnlock;

        public ZooLayoutState LayoutState => layoutState;
        public ZooCareMeterState CareMeterState => careMeterState;

        /// <summary>Every placed habitat's root GameObject, in placement order -- read-only exposure for HabitatOcclusionFader (Phase 11), which needs to frustum-test each one against the focused habitat without ZooManager knowing anything about rendering.</summary>
        public IReadOnlyList<GameObject> InstantiatedHabitats => instantiatedHabitats;

        /// <summary>Monetization model: 9 regular + 1 mythic animal free, the rest behind a one-time full-roster unlock (AnimalDefinition.IsIntroductory marks the free set). Gates OfferGenerator's candidate pool.</summary>
        public bool HasFullUnlock => hasFullUnlock;

        /// <summary>Raised whenever a new 3-slot offer is ready to display (Offer Tableau UI, section 3).</summary>
        public event Action<OfferTableau> OnTableauReady;

        /// <summary>Raised the moment the shared Care Meter fills (section 6/7).</summary>
        public event Action OnCareMeterComplete;

        /// <summary>Raised after each new habitat is placed and recorded, so CameraRig (section 11) can refresh its framing.</summary>
        public event Action<PlotCoordinate> OnHabitatPlaced;

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
            var save = animalRoster != null ? SaveSystem.Load() : null;
            bool isFreshZoo = save == null;

            if (!isFreshZoo)
            {
                RestoreFromSave(save);
            }
            else if (economyConfig != null)
            {
                careMeterState.StartNextCycle(economyConfig.Threshold(1));
            }

            if (animalRoster != null && economyConfig != null)
            {
                offerGenerator = new OfferGenerator(animalRoster, economyConfig);

                // Doc: the tableau is due "at game start, and every time the zoo's shared Care
                // Meter fills" -- a restored save already has a zoo in progress, so re-entering
                // the app should drop the player straight back into it, not re-present a choice
                // they're not due yet. The next tableau still arrives normally the next time
                // ReportInteractionHearts completes the Care Meter.
                if (isFreshZoo)
                {
                    RequestNextTableau();
                }
            }
            else
            {
                Debug.LogWarning("animalRoster or economyConfig unassigned -- Offer Tableau will not be requested.", this);
            }
        }

        /// <summary>
        /// Replays a save's placements through the normal PlaceAnimal path -- so plots, NavMesh
        /// bakes, and AnimalController wiring all happen exactly as they would live -- then
        /// restores the Care Meter's exact heart count/threshold on top, since PlaceAnimal itself
        /// never touches ZooCareMeterState. Autosaving is suppressed for the duration so replaying
        /// N placements doesn't trigger N redundant writes of data we just loaded.
        /// </summary>
        private void RestoreFromSave(SaveSystem.SaveData save)
        {
            isRestoringFromSave = true;
            foreach (var entry in save.layout.placedAnimals)
            {
                var definition = animalRoster.FindById(entry.animalDefinitionId);
                if (definition == null)
                {
                    Debug.LogError($"[ZooManager] Save references unknown animal id '{entry.animalDefinitionId}' -- skipping (was it removed from the Animal Roster?). Plots for any later-placed animals in this save will no longer line up with where they originally were.", this);
                    continue;
                }
                PlaceAnimal(definition);
            }
            isRestoringFromSave = false;

            careMeterState.currentHearts = save.currentHearts;
            careMeterState.currentThreshold = save.currentThreshold;
            hasFullUnlock = save.hasFullUnlock;
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) SaveNow();
        }

        private void SaveNow()
        {
            if (isRestoringFromSave) return;
            SaveSystem.Save(layoutState, careMeterState, hasFullUnlock);
        }

        /// <summary>
        /// Flips the local full-unlock flag and saves. This is NOT a real purchase -- there is no
        /// payment processing here. Wiring this to an actual store transaction (Unity IAP plus App
        /// Store/Google Play product configuration, which needs the developer's own store
        /// accounts) is separate work this method deliberately does not attempt.
        /// </summary>
        public void UnlockFullRoster()
        {
            if (hasFullUnlock) return;
            hasFullUnlock = true;
            SaveNow();
        }

        /// <summary>
        /// Reset Zoo (design doc section 12/14): deletes the save (primary + backup) and reloads
        /// the scene, which cleanly resets every runtime system -- Care Meter, layout, placed
        /// habitats, camera framing -- to a fresh empty zoo. Simpler and more robust than manually
        /// tearing down each subsystem by hand. Irreversible; the caller (SettingsUIController) is
        /// responsible for confirming with the player first.
        /// </summary>
        public void ResetZoo()
        {
            SaveSystem.DeleteSave();
            // By name, not buildIndex -- reloads correctly in the Editor even if the scene hasn't
            // been added to Build Settings yet, which buildIndex (-1 when unregistered) would not.
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
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
            OnHabitatPlaced?.Invoke(plot);
            SaveNow();

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

            // Phase 9 perf pass: this per-placement bake was a documented open question (see the
            // summary above) rather than a measured one -- log the actual cost each time so it's
            // empirically checkable in the Console instead of just assumed cheap.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            surface.BuildNavMesh();
            stopwatch.Stop();
            Debug.Log($"[Perf] NavMesh bake for '{habitat.name}' took {stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
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
            var toyController = habitat.AddComponent<ToyController>();
            toyController.Initialize(economyConfig);

            // Phase 9 perf pass: pauses this habitat's wander/audio polling and disables its
            // containment Walls whenever it's outside the camera's frustum.
            habitat.AddComponent<HabitatVisibilityLod>();
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

            if (careMeterState.IsComplete)
            {
                OnCareMeterComplete?.Invoke();
                foreach (var habitat in instantiatedHabitats)
                {
                    var runtime = habitat.GetComponent<HabitatRuntime>();
                    runtime?.Animal?.PlayCelebration();
                }

                careMeterState.StartNextCycle(economyConfig.Threshold(layoutState.Count + 1));
                RequestNextTableau();
            }

            SaveNow();
        }

        /// <summary>Last tableau generated -- lets a UI that subscribes late (GameObject/script
        /// init order between separate objects isn't guaranteed) catch up immediately instead
        /// of waiting for the next placement.</summary>
        public OfferTableau CurrentTableau { get; private set; }

        private void RequestNextTableau()
        {
            if (offerGenerator == null) return;
            CurrentTableau = offerGenerator.GenerateOffer(layoutState, fullUnlockActive: hasFullUnlock);
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
