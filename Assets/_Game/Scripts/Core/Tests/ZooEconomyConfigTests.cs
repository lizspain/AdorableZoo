using NUnit.Framework;
using UnityEngine;
using RainbowZoo.Core;

namespace RainbowZoo.Tests
{
    public class ZooEconomyConfigTests
    {
        private ZooEconomyConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<ZooEconomyConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
        }

        // Design doc "Zoo-Wide Difficulty Scaling" worked example: BaseThreshold=10.5,
        // GrowthPerAnimal=1.4, AccelPerAnimal=0.6 (the class defaults) -> 10, 12, 16, 20, 26, 32, 40
        // for animals-owned 1..7. n=1 (10.5) and n=6 (32.5) are the doc's explicit
        // round-half-to-even examples.
        [TestCase(1, 10)]
        [TestCase(2, 12)]
        [TestCase(3, 16)]
        [TestCase(4, 20)]
        [TestCase(5, 26)]
        [TestCase(6, 32)]
        [TestCase(7, 40)]
        public void Threshold_MatchesDesignDocWorkedExample(int animalsOwned, int expectedHearts)
        {
            Assert.AreEqual(expectedHearts, config.Threshold(animalsOwned));
        }

        [Test]
        public void Threshold_IsMonotonicallyNonDecreasing()
        {
            int previous = config.Threshold(1);
            for (int n = 2; n <= 20; n++)
            {
                int current = config.Threshold(n);
                Assert.GreaterOrEqual(current, previous, $"Threshold decreased at animalsOwned={n}");
                previous = current;
            }
        }

        [Test]
        public void Threshold_ThrowsForFewerThanOneAnimal()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => config.Threshold(0));
        }
    }
}
