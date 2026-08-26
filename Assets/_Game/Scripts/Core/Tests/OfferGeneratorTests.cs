using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using RainbowZoo.Core;

namespace RainbowZoo.Tests
{
    public class OfferGeneratorTests
    {
        private AnimalRoster roster;
        private ZooEconomyConfig config;
        private AnimalDefinition[] standards;
        private AnimalDefinition mythical;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<ZooEconomyConfig>();

            standards = new AnimalDefinition[4];
            for (int i = 0; i < standards.Length; i++)
            {
                standards[i] = ScriptableObject.CreateInstance<AnimalDefinition>();
                standards[i].ConfigureForTests($"standard-{i}", false);
            }

            mythical = ScriptableObject.CreateInstance<AnimalDefinition>();
            mythical.ConfigureForTests("mythical-0", true);

            roster = ScriptableObject.CreateInstance<AnimalRoster>();
            var rosterField = typeof(AnimalRoster).GetField("animals",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            rosterField.SetValue(roster, standards.Append(mythical).ToList());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var a in standards) UnityEngine.Object.DestroyImmediate(a);
            UnityEngine.Object.DestroyImmediate(mythical);
            UnityEngine.Object.DestroyImmediate(roster);
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void GenerateOffer_MythicalRollRateMatchesConfiguredProbability()
        {
            var layout = new ZooLayoutState();
            var random = new System.Random(12345);
            const int trials = 5000;
            int mythicalCount = 0;

            for (int i = 0; i < trials; i++)
            {
                var offer = generator().GenerateOffer(layout, random);
                if (Enumerable.Range(0, OfferTableau.SlotCount).Any(s => offer.GetSlot(s) != null && offer.GetSlot(s).IsMythical))
                {
                    mythicalCount++;
                }
            }

            float observedRate = mythicalCount / (float)trials;
            Assert.AreEqual(config.MythicalProbability, observedRate, 0.02f,
                $"Observed mythical rate {observedRate:P1} too far from configured {config.MythicalProbability:P1} over {trials} trials.");
        }

        [Test]
        public void GenerateOffer_NeverRepeatsASpeciesWithinOneTableau()
        {
            var layout = new ZooLayoutState();
            var random = new System.Random(999);

            for (int i = 0; i < 500; i++)
            {
                var offer = generator().GenerateOffer(layout, random);
                var ids = Enumerable.Range(0, OfferTableau.SlotCount)
                    .Select(s => offer.GetSlot(s))
                    .Where(a => a != null)
                    .Select(a => a.Id)
                    .ToList();

                Assert.AreEqual(ids.Count, ids.Distinct().Count(), "Tableau contained a duplicate species.");
            }
        }

        [Test]
        public void GenerateOffer_OwnedAnimalsAppearLessOftenThanUnownedPeers()
        {
            var layout = new ZooLayoutState();
            layout.placedAnimals.Add(new AnimalSaveState(standards[0].Id, 1, 1));

            var random = new System.Random(42);
            const int trials = 8000;
            int ownedAppearances = 0;
            int unownedPeerAppearances = 0;

            for (int i = 0; i < trials; i++)
            {
                var offer = generator().GenerateOffer(layout, random);
                for (int s = 0; s < OfferTableau.SlotCount; s++)
                {
                    var slot = offer.GetSlot(s);
                    if (slot == null) continue;
                    if (slot.Id == standards[0].Id) ownedAppearances++;
                    if (slot.Id == standards[1].Id) unownedPeerAppearances++;
                }
            }

            Assert.Greater(unownedPeerAppearances, ownedAppearances,
                $"Expected the unowned peer ({unownedPeerAppearances}) to appear more often than the owned animal ({ownedAppearances}) given OwnedAnimalWeightMultiplier={config.OwnedAnimalWeightMultiplier}.");

            float ratio = ownedAppearances / (float)unownedPeerAppearances;
            Assert.AreEqual(config.OwnedAnimalWeightMultiplier, ratio, 0.1f,
                $"Owned/unowned appearance ratio {ratio:F2} too far from the configured weight multiplier {config.OwnedAnimalWeightMultiplier:F2}.");
        }

        [Test]
        public void GenerateOffer_WhenLocked_OnlyOffersIntroductoryAnimals()
        {
            standards[0].ConfigureForTests("standard-0", false, isIntroductory: true);
            // standards[1..3] and mythical stay isIntroductory=false from SetUp.

            var layout = new ZooLayoutState();
            var random = new System.Random(7);

            for (int i = 0; i < 500; i++)
            {
                var offer = generator().GenerateOffer(layout, random, fullUnlockActive: false);
                for (int s = 0; s < OfferTableau.SlotCount; s++)
                {
                    var slot = offer.GetSlot(s);
                    if (slot == null) continue;
                    Assert.AreEqual(standards[0].Id, slot.Id,
                        $"Locked roster should only ever offer the introductory animal, but offered '{slot.Id}'.");
                }
            }
        }

        [Test]
        public void GenerateOffer_WhenUnlocked_CanOfferNonIntroductoryAnimals()
        {
            standards[0].ConfigureForTests("standard-0", false, isIntroductory: true);

            var layout = new ZooLayoutState();
            var random = new System.Random(7);
            bool sawNonIntroductory = false;

            for (int i = 0; i < 500 && !sawNonIntroductory; i++)
            {
                var offer = generator().GenerateOffer(layout, random, fullUnlockActive: true);
                for (int s = 0; s < OfferTableau.SlotCount; s++)
                {
                    var slot = offer.GetSlot(s);
                    if (slot != null && slot.Id != standards[0].Id)
                    {
                        sawNonIntroductory = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(sawNonIntroductory, "Expected the unlocked roster to eventually offer a non-introductory animal.");
        }

        private OfferGenerator generator() => new OfferGenerator(roster, config);
    }
}
