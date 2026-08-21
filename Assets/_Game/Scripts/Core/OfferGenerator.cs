using System;
using System.Collections.Generic;
using System.Linq;

namespace RainbowZoo.Core
{
    /// <summary>
    /// Builds each 3-slot OfferTableau: a weighted mythical roll, then weighted selection from
    /// the standard pool (animals already owned get half weight -- doc: "Offer Tableau & Mythical
    /// Roll"). Pure function of roster/layout/config; a System.Random can be injected for tests.
    /// </summary>
    public sealed class OfferGenerator
    {
        private readonly AnimalRoster roster;
        private readonly ZooEconomyConfig config;

        public OfferGenerator(AnimalRoster roster, ZooEconomyConfig config)
        {
            this.roster = roster;
            this.config = config;
        }

        public OfferTableau GenerateOffer(ZooLayoutState layoutState, Random random = null)
        {
            random ??= new Random();

            var ownedIds = new HashSet<string>(layoutState.placedAnimals.Select(a => a.animalDefinitionId));
            var standards = roster.Animals.Where(a => !a.IsMythical).ToList();
            var mythicals = roster.Animals.Where(a => a.IsMythical).ToList();

            var slots = new AnimalDefinition[OfferTableau.SlotCount];
            int mythicalSlotIndex = -1;

            if (mythicals.Count > 0 && random.NextDouble() < config.MythicalProbability)
            {
                mythicalSlotIndex = random.Next(OfferTableau.SlotCount);
                slots[mythicalSlotIndex] = mythicals[random.Next(mythicals.Count)];
            }

            var pool = new List<AnimalDefinition>(standards);
            for (int i = 0; i < OfferTableau.SlotCount; i++)
            {
                if (i == mythicalSlotIndex) continue;
                slots[i] = WeightedPickAndRemove(pool, ownedIds, random);
            }

            return new OfferTableau(slots[0], slots[1], slots[2]);
        }

        /// <summary>Picks one entry from pool (weighted, owned animals at OwnedAnimalWeightMultiplier) and removes it, so a tableau never repeats a species. Returns null if pool is empty.</summary>
        private AnimalDefinition WeightedPickAndRemove(List<AnimalDefinition> pool, HashSet<string> ownedIds, Random random)
        {
            if (pool.Count == 0) return null;

            var weights = new float[pool.Count];
            float totalWeight = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                weights[i] = ownedIds.Contains(pool[i].Id) ? config.OwnedAnimalWeightMultiplier : 1f;
                totalWeight += weights[i];
            }

            int chosenIndex = pool.Count - 1;
            if (totalWeight <= 0f)
            {
                chosenIndex = random.Next(pool.Count);
            }
            else
            {
                double roll = random.NextDouble() * totalWeight;
                double cumulative = 0d;
                for (int i = 0; i < weights.Length; i++)
                {
                    cumulative += weights[i];
                    if (roll < cumulative)
                    {
                        chosenIndex = i;
                        break;
                    }
                }
            }

            var chosen = pool[chosenIndex];
            pool.RemoveAt(chosenIndex);
            return chosen;
        }
    }
}
