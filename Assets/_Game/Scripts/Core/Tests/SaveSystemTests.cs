using System;
using System.IO;
using NUnit.Framework;
using RainbowZoo.Core;
using RainbowZoo.Save;

namespace RainbowZoo.Tests
{
    public class SaveSystemTests
    {
        private string testDirectory;
        private string originalSaveDirectory;

        [SetUp]
        public void SetUp()
        {
            originalSaveDirectory = SaveSystem.SaveDirectory;
            testDirectory = Path.Combine(Path.GetTempPath(), "RainbowZooSaveTests_" + Guid.NewGuid().ToString("N"));
            SaveSystem.SaveDirectory = testDirectory;
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.SaveDirectory = originalSaveDirectory;
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        private static ZooLayoutState BuildLayout(params string[] animalIds)
        {
            var layout = new ZooLayoutState();
            foreach (var id in animalIds)
            {
                layout.PlaceNext(id);
            }
            return layout;
        }

        private static ZooCareMeterState BuildCareMeter(int hearts, int threshold)
        {
            var careMeter = new ZooCareMeterState();
            careMeter.StartNextCycle(threshold);
            careMeter.AddHearts(hearts);
            return careMeter;
        }

        [Test]
        public void SaveThenLoad_RoundTripsLayoutAndCareMeter()
        {
            var layout = BuildLayout("cat", "zebra", "tiger");
            var careMeter = BuildCareMeter(hearts: 7, threshold: 16);

            SaveSystem.Save(layout, careMeter);
            var loaded = SaveSystem.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(7, loaded.currentHearts);
            Assert.AreEqual(16, loaded.currentThreshold);
            Assert.AreEqual(3, loaded.layout.placedAnimals.Count);
            Assert.AreEqual("cat", loaded.layout.placedAnimals[0].animalDefinitionId);
            Assert.AreEqual("zebra", loaded.layout.placedAnimals[1].animalDefinitionId);
            Assert.AreEqual("tiger", loaded.layout.placedAnimals[2].animalDefinitionId);
            Assert.AreEqual(1, loaded.layout.placedAnimals[0].plotColumn);
            Assert.AreEqual(1, loaded.layout.placedAnimals[0].plotRow);
        }

        [Test]
        public void Load_WithNoSaveFileYet_ReturnsNull()
        {
            Assert.IsNull(SaveSystem.Load());
        }

        [Test]
        public void Load_FallsBackToBackup_WhenPrimaryFileIsCorrupted()
        {
            var firstLayout = BuildLayout("cat");
            var firstCareMeter = BuildCareMeter(hearts: 3, threshold: 10);
            SaveSystem.Save(firstLayout, firstCareMeter);

            // A second save promotes the first save into the backup slot.
            var secondLayout = BuildLayout("cat", "zebra");
            var secondCareMeter = BuildCareMeter(hearts: 9, threshold: 12);
            SaveSystem.Save(secondLayout, secondCareMeter);

            // Simulate an interrupted/corrupted write landing in the primary save file.
            File.WriteAllText(Path.Combine(testDirectory, "zoo_save.json"), "{ not valid json");

            var loaded = SaveSystem.Load();

            Assert.IsNotNull(loaded, "Expected fallback to the backup save, not a null result.");
            Assert.AreEqual(3, loaded.currentHearts, "Expected the backup (first save) hearts, not the corrupted primary's.");
            Assert.AreEqual(1, loaded.layout.placedAnimals.Count);
        }

        [Test]
        public void Load_ReturnsNull_WhenPrimaryCorruptedAndNoBackupExists()
        {
            Directory.CreateDirectory(testDirectory);
            File.WriteAllText(Path.Combine(testDirectory, "zoo_save.json"), "{ not valid json");

            Assert.IsNull(SaveSystem.Load());
        }
    }
}
