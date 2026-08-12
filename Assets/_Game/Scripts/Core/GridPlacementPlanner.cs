using System.Collections.Generic;

namespace RainbowZoo.Core
{
    public readonly struct PlotCoordinate
    {
        public readonly int Column;
        public readonly int Row;

        public PlotCoordinate(int column, int row)
        {
            Column = column;
            Row = row;
        }

        public override string ToString() => $"(col={Column}, row={Row})";
    }

    /// <summary>
    /// Produces the zoo's plot fill order (design doc, "Spatial Plot Layout Architecture"):
    /// expanding-square 2x2 -> 3x3 -> columns 4-5 to complete the initial 5x3 grid, then
    /// alternating new-column (top-to-bottom) / new-row (left-to-right) growth beyond that.
    /// Columns/rows are 1-indexed; column 1 = leftmost, row 1 = topmost.
    /// </summary>
    public static class GridPlacementPlanner
    {
        public static PlotCoordinate GetPlotCoordinate(int zeroBasedIndex)
        {
            var enumerator = EnumeratePlots().GetEnumerator();
            for (int i = 0; i <= zeroBasedIndex; i++)
            {
                enumerator.MoveNext();
            }
            return enumerator.Current;
        }

        public static IEnumerable<PlotCoordinate> EnumeratePlots()
        {
            // Phase 1: 2x2 sub-grid (columns 1-2, rows 1-2), left to right, top to bottom.
            for (int row = 1; row <= 2; row++)
            {
                for (int col = 1; col <= 2; col++)
                {
                    yield return new PlotCoordinate(col, row);
                }
            }

            // Phase 2: expand to 3x3 (columns 1-3, rows 1-3), left to right, top to bottom,
            // skipping the cells the 2x2 phase already filled.
            for (int row = 1; row <= 3; row++)
            {
                for (int col = 1; col <= 3; col++)
                {
                    if (col <= 2 && row <= 2) continue;
                    yield return new PlotCoordinate(col, row);
                }
            }

            // Phase 3: columns 4 then 5, each completed top to bottom before the next begins.
            for (int col = 4; col <= 5; col++)
            {
                for (int row = 1; row <= 3; row++)
                {
                    yield return new PlotCoordinate(col, row);
                }
            }

            // Phase 4: beyond the 5x3 grid, alternate appending a new column (top to bottom)
            // and a new row (left to right), each one cell larger than the last of its kind.
            int columns = 5;
            int rows = 3;
            bool nextIsColumn = true;
            while (true)
            {
                if (nextIsColumn)
                {
                    columns++;
                    for (int row = 1; row <= rows; row++)
                    {
                        yield return new PlotCoordinate(columns, row);
                    }
                }
                else
                {
                    rows++;
                    for (int col = 1; col <= columns; col++)
                    {
                        yield return new PlotCoordinate(col, rows);
                    }
                }
                nextIsColumn = !nextIsColumn;
            }
        }
    }
}
