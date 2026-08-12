using System;
using System.Collections.Generic;

namespace RainbowZoo.Core
{
    /// <summary>
    /// Which AnimalDefinition (by stable Id, not object reference -- this gets JSON-serialized
    /// by SaveSystem) occupies a given plot. Carries no heart/progress data of its own; care
    /// progress belongs to the zoo (ZooCareMeterState), not the animal.
    /// </summary>
    [Serializable]
    public sealed class AnimalSaveState
    {
        public string animalDefinitionId;
        public int plotColumn;
        public int plotRow;

        public AnimalSaveState(string animalDefinitionId, int plotColumn, int plotRow)
        {
            this.animalDefinitionId = animalDefinitionId;
            this.plotColumn = plotColumn;
            this.plotRow = plotRow;
        }
    }

    /// <summary>
    /// The zoo's plot grid: every animal placed so far and where. ZooManager is the sole
    /// runtime owner/writer of this state (one-way data flow -- see Technical Design doc).
    /// </summary>
    [Serializable]
    public sealed class ZooLayoutState
    {
        public List<AnimalSaveState> placedAnimals = new List<AnimalSaveState>();

        public int Count => placedAnimals.Count;

        /// <summary>Plot the next placed animal will occupy, per the expanding-square/growth fill order.</summary>
        public PlotCoordinate NextPlotCoordinate()
        {
            return GridPlacementPlanner.GetPlotCoordinate(placedAnimals.Count);
        }

        public AnimalSaveState PlaceNext(string animalDefinitionId)
        {
            var plot = NextPlotCoordinate();
            var entry = new AnimalSaveState(animalDefinitionId, plot.Column, plot.Row);
            placedAnimals.Add(entry);
            return entry;
        }
    }
}
