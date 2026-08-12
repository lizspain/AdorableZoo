using UnityEngine;

namespace RainbowZoo.Core
{
    [CreateAssetMenu(menuName = "Rainbow Zoo/Animal Definition", fileName = "NewAnimalDefinition")]
    public sealed class AnimalDefinition : ScriptableObject
    {
        [Tooltip("Stable identifier used for save data and pool lookups. Never rename after animals have been saved with it.")]
        [SerializeField] private string id;

        [SerializeField] private string displayName;

        [Header("Prefabs")]
        [Tooltip("The Agent-style prefab (NavMeshAgent + ControllerPetZoo) placed into the habitat.")]
        [SerializeField] private GameObject animalPrefab;

        [Tooltip("Optional per-species habitat variant extending the base Habitat prefab. Null uses the shared base habitat.")]
        [SerializeField] private GameObject habitatPrefabOverride;

        [SerializeField] private RuntimeAnimatorController animatorController;

        [Tooltip("Child transform on animalPrefab the shared Toy parents to while being carried.")]
        [SerializeField] private Transform attachmentPoint;

        [Header("Toy Appearance")]
        [Tooltip("Mesh/material the shared zoo Toy swaps to when playing with this species. Same Toy object, different skin.")]
        [SerializeField] private ToyAppearance toyAppearance;

        [Header("VFX")]
        [SerializeField] private GameObject petVfx;
        [SerializeField] private GameObject playVfx;
        [SerializeField] private GameObject feedVfx;
        [SerializeField] private GameObject celebrationVfx;

        [Header("Rarity")]
        [SerializeField] private bool isMythical;
        [SerializeField] private string rarityTag = "standard";

        public string Id => id;
        public string DisplayName => displayName;
        public GameObject AnimalPrefab => animalPrefab;
        public GameObject HabitatPrefabOverride => habitatPrefabOverride;
        public RuntimeAnimatorController AnimatorController => animatorController;
        public Transform AttachmentPoint => attachmentPoint;
        public ToyAppearance ToyAppearance => toyAppearance;
        public GameObject PetVfx => petVfx;
        public GameObject PlayVfx => playVfx;
        public GameObject FeedVfx => feedVfx;
        public GameObject CelebrationVfx => celebrationVfx;
        public bool IsMythical => isMythical;
        public string RarityTag => rarityTag;
    }

    [System.Serializable]
    public struct ToyAppearance
    {
        public Mesh mesh;
        public Material[] materials;
    }
}
