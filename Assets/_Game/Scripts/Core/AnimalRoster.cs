using System.Collections.Generic;
using UnityEngine;

namespace RainbowZoo.Core
{
    /// <summary>Every AnimalDefinition available to be offered -- the pool OfferGenerator draws from.</summary>
    [CreateAssetMenu(menuName = "Rainbow Zoo/Animal Roster", fileName = "AnimalRoster")]
    public sealed class AnimalRoster : ScriptableObject
    {
        [SerializeField] private List<AnimalDefinition> animals = new List<AnimalDefinition>();

        public IReadOnlyList<AnimalDefinition> Animals => animals;

        /// <summary>Resolves a save file's stable animalDefinitionId back to its asset. Null if the id no longer exists in the roster (e.g. an animal was removed after being saved).</summary>
        public AnimalDefinition FindById(string id)
        {
            foreach (var animal in animals)
            {
                if (animal != null && animal.Id == id) return animal;
            }
            return null;
        }
    }
}
