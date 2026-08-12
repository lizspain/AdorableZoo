using System.Collections.Generic;
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
        [SerializeField] private Vector3 gridOrigin = Vector3.zero;
        [Tooltip("World-space distance between adjacent plot centers. +X per column, +Z per row.")]
        [SerializeField] private float plotSpacing = 4f;

        [Header("Debug (Phase 2 placeholder -- replaced by the real Offer Tableau in Phase 3)")]
        [SerializeField] private List<AnimalDefinition> debugAnimalPool = new List<AnimalDefinition>();

        private readonly ZooLayoutState layoutState = new ZooLayoutState();
        private readonly ZooCareMeterState careMeterState = new ZooCareMeterState();
        private readonly List<GameObject> instantiatedHabitats = new List<GameObject>();
        private int debugPoolCursor;

        public ZooLayoutState LayoutState => layoutState;
        public ZooCareMeterState CareMeterState => careMeterState;

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
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                PlaceNextDebugAnimal();
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

            layoutState.PlaceNext(definition.Id);
            return habitat;
        }

        public Vector3 PlotToWorld(PlotCoordinate plot)
        {
            return gridOrigin + new Vector3((plot.Column - 1) * plotSpacing, 0f, (plot.Row - 1) * plotSpacing);
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
