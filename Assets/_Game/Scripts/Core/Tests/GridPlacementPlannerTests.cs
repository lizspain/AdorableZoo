using NUnit.Framework;
using RainbowZoo.Core;

namespace RainbowZoo.Tests
{
    public class GridPlacementPlannerTests
    {
        // Design doc "Spatial Plot Layout Architecture" worked example: the exact 15-plot
        // fill order for the initial 5x3 grid (columns 1-5, rows 1-3), 0-indexed here.
        private static readonly (int col, int row)[] ExpectedFirst15 =
        {
            (1, 1), (2, 1), (1, 2), (2, 2),                     // 2x2 phase
            (3, 1), (3, 2), (1, 3), (2, 3), (3, 3),             // 3x3 new-cells phase
            (4, 1), (4, 2), (4, 3), (5, 1), (5, 2), (5, 3),     // columns 4 then 5
        };

        [Test]
        public void GetPlotCoordinate_MatchesDesignDocFirst15Plots()
        {
            for (int i = 0; i < ExpectedFirst15.Length; i++)
            {
                var plot = GridPlacementPlanner.GetPlotCoordinate(i);
                Assert.AreEqual(ExpectedFirst15[i].col, plot.Column, $"Plot #{i + 1} column mismatch");
                Assert.AreEqual(ExpectedFirst15[i].row, plot.Row, $"Plot #{i + 1} row mismatch");
            }
        }

        [Test]
        public void GetPlotCoordinate_GrowsByAlternatingColumnThenRowBeyond15Plots()
        {
            // Doc: "growth continues by appending a new column to the right, filled top to
            // bottom; once that new column is completely filled, a new row is appended
            // instead, filled left to right, before the next column begins."

            // Plots 16-18 (index 15-17): new column 6, rows 1-3, top to bottom.
            Assert.AreEqual((6, 1), AsTuple(15));
            Assert.AreEqual((6, 2), AsTuple(16));
            Assert.AreEqual((6, 3), AsTuple(17));

            // Plots 19-24 (index 18-23): new row 4, across columns 1-6, left to right.
            Assert.AreEqual((1, 4), AsTuple(18));
            Assert.AreEqual((6, 4), AsTuple(23));

            // Plots 25-28 (index 24-27): new column 7, now 4 rows tall, top to bottom.
            Assert.AreEqual((7, 1), AsTuple(24));
            Assert.AreEqual((7, 4), AsTuple(27));

            // Plots 29-35 (index 28-34): new row 5, across columns 1-7, left to right.
            Assert.AreEqual((1, 5), AsTuple(28));
            Assert.AreEqual((7, 5), AsTuple(34));
        }

        [Test]
        public void GetPlotCoordinate_NeverRepeatsAPlot()
        {
            var seen = new System.Collections.Generic.HashSet<(int, int)>();
            for (int i = 0; i < 200; i++)
            {
                var plot = GridPlacementPlanner.GetPlotCoordinate(i);
                Assert.IsTrue(seen.Add((plot.Column, plot.Row)), $"Plot {plot} repeated at index {i}");
            }
        }

        private static (int, int) AsTuple(int zeroBasedIndex)
        {
            var plot = GridPlacementPlanner.GetPlotCoordinate(zeroBasedIndex);
            return (plot.Column, plot.Row);
        }
    }
}
