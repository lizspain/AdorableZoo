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
    }
}
