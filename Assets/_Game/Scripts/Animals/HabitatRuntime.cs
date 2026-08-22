using RainbowZoo.Core;
using UnityEngine;

namespace RainbowZoo.Animals
{
    /// <summary>
    /// Marker + child-reference cache added to every instantiated habitat (ZooManager), so
    /// InputRouter and ToyController can resolve "which habitat/animal does this touch belong
    /// to" via GetComponentInParent from whatever collider was hit (Floor, Walls, FoodDish, or
    /// the animal's own body), without re-doing child name lookups every frame.
    /// </summary>
    public sealed class HabitatRuntime : MonoBehaviour
    {
        /// <summary>
        /// Half the habitat footprint's side length (matches HabitatPrefabBuilder's 4x4 Floor/
        /// Walls) -- the single source of truth both the habitat-authoring tool and any runtime
        /// bounds-clamping (e.g. keeping the dragged Toy inside the walls) build from.
        /// </summary>
        public const float HalfExtent = 2f;

        public AnimalController Animal { get; private set; }
        public Transform ToyDropPoint { get; private set; }
        public Transform FoodDish { get; private set; }

        /// <summary>This habitat's own Toy (one per habitat, not shared zoo-wide -- see ToyController).</summary>
        public ToyController Toy => GetComponent<ToyController>();

        public void Initialize(AnimalController animal)
        {
            Animal = animal;
            ToyDropPoint = transform.Find("ToyDropPoint");
            FoodDish = transform.Find("FoodDish");

            if (ToyDropPoint == null)
            {
                Debug.LogError($"Habitat '{name}' has no child named 'ToyDropPoint'.", this);
            }
            if (FoodDish == null)
            {
                Debug.LogError($"Habitat '{name}' has no child named 'FoodDish'.", this);
            }
        }
    }
}
