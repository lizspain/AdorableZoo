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

        [Tooltip("Local position offset from AttachmentPoint where the carried Toy actually sits -- a bone's own pivot isn't always where a toy should visually rest (e.g. a head bone's pivot can be at the top of the skull, not near the mouth). Set via Rainbow Zoo > Content > Toy Attachment Preview, not hand-typed.")]
        [SerializeField] private Vector3 toyAttachmentOffset;
        [Tooltip("Local rotation offset (Euler degrees) from AttachmentPoint for the carried Toy.")]
        [SerializeField] private Vector3 toyAttachmentRotationOffset;

        [Header("Toy Appearance")]
        [Tooltip("Mesh/material the shared zoo Toy swaps to when playing with this species. Same Toy object, different skin.")]
        [SerializeField] private ToyAppearance toyAppearance;

        [Header("VFX")]
        [SerializeField] private GameObject petVfx;
        [SerializeField] private GameObject playVfx;
        [SerializeField] private GameObject feedVfx;
        [SerializeField] private GameObject celebrationVfx;

        [Header("SFX (optional -- every action must already read clearly from animation/VFX alone)")]
        [Tooltip("Purr/chirp on Rest.")]
        [SerializeField] private AudioClip petSfx;
        [Tooltip("Giggle/bark while chasing the toy.")]
        [SerializeField] private AudioClip playSfx;
        [Tooltip("Chomp on Eat.")]
        [SerializeField] private AudioClip feedSfx;
        [Tooltip("Played on the Jump celebration for a single heart-gain (Pet/Feed/Play). Not played for the zoo-wide Care Meter completion beat -- that uses AudioDirector's tableau fanfare instead, so N placed animals don't layer this clip on top of each other.")]
        [SerializeField] private AudioClip celebrationSfx;

        [Header("Rarity")]
        [SerializeField] private bool isMythical;
        [SerializeField] private string rarityTag = "standard";

        [Header("Access")]
        [Tooltip("True for animals included in the free starter roster. False means this animal only becomes available after the full-unlock purchase. Not yet enforced anywhere at runtime -- OfferGenerator/purchase-state gating is separate, later work; this is just the data flag.")]
        [SerializeField] private bool isIntroductory;

        public string Id => id;
        public string DisplayName => displayName;
        public GameObject AnimalPrefab => animalPrefab;
        public GameObject HabitatPrefabOverride => habitatPrefabOverride;
        public RuntimeAnimatorController AnimatorController => animatorController;
        public Transform AttachmentPoint => attachmentPoint;
        public Vector3 ToyAttachmentOffset => toyAttachmentOffset;
        public Vector3 ToyAttachmentRotationOffset => toyAttachmentRotationOffset;
        public ToyAppearance ToyAppearance => toyAppearance;
        public GameObject PetVfx => petVfx;
        public GameObject PlayVfx => playVfx;
        public GameObject FeedVfx => feedVfx;
        public GameObject CelebrationVfx => celebrationVfx;
        public AudioClip PetSfx => petSfx;
        public AudioClip PlaySfx => playSfx;
        public AudioClip FeedSfx => feedSfx;
        public AudioClip CelebrationSfx => celebrationSfx;
        public bool IsMythical => isMythical;
        public string RarityTag => rarityTag;
        public bool IsIntroductory => isIntroductory;

        /// <summary>Test-only construction hook (Game.Core.Tests only, via InternalsVisibleTo) -- AnimalDefinition assets are otherwise authored exclusively through the Inspector.</summary>
        internal void ConfigureForTests(string id, bool isMythical, bool isIntroductory = false)
        {
            this.id = id;
            this.isMythical = isMythical;
            this.isIntroductory = isIntroductory;
        }
    }

    [System.Serializable]
    public struct ToyAppearance
    {
        public Mesh mesh;
        public Material[] materials;
    }
}
