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
        public AnimalController Animal { get; private set; }
        public Transform ToyDropPoint { get; private set; }
        public Transform FoodDish { get; private set; }

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
