using System.Collections.Generic;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class PixelAxisTests
    {
        const float Left = 10;

        [Fact]
        public void Constructor_PutsTheEdgeOnAWholePixelAndTheWidthInWholePixels()
        {
            var axis = new PixelAxis(new DisplayWindow(0, 1, 100), 10.7f, 99.9f);

            Assert.Equal(10, axis.Left);
            Assert.Equal(99, axis.Width);
        }

        [Fact]
        public void EvenlyCombinedColumns_FallTenToAPixel()
        {
            var axis = new PixelAxis(new DisplayWindow(0, 3, 1000), Left, 100);

            Assert.True(axis.Combines);
            Assert.False(axis.SingleColumnCells);
            Assert.Equal(30, axis.CellSpan);
            Assert.Equal(30, axis.WidestCell);
            for (int pixel = 0; pixel < axis.Width; pixel++)
            {
                Assert.Equal((10 * pixel, 10 * pixel + 10), axis.ColumnsAt(pixel));
                Assert.Equal(Left + pixel, axis.ColumnX(10 * pixel + 9));
            }
        }

        [Theory]
        [InlineData(1000, 300)]
        [InlineData(1001, 1000)]
        [InlineData(777, 100)]
        public void CombinedColumns_AreSplitAmongThePixelsWithoutGapOrOverlap(int columns, int width)
        {
            var axis = new PixelAxis(new DisplayWindow(0, 1, columns), Left, width);

            var next = 0;
            for (int pixel = 0; pixel < width; pixel++)
            {
                var (start, end) = axis.ColumnsAt(pixel);
                Assert.Equal(next, start);
                Assert.InRange(end - start, columns / width, (columns + width - 1) / width);
                for (int column = start; column < end; column++)
                    Assert.Equal(pixel, axis.PixelOf(column));

                next = end;
            }

            Assert.Equal(columns, next);
            Assert.Equal((columns + width - 1) / width, axis.WidestCell);
        }

        // NB: cells are counted from the sweep origin, so a window panned by whole cells shows the same columns
        // together, and the plot does not shimmer as it is panned.
        [Fact]
        public void PanningByWholeCells_KeepsTheSameColumnsTogether()
        {
            var window = new DisplayWindow(0, 1, 1000);
            var axis = new PixelAxis(window, Left, 300);
            var boundary = axis.Boundary(437, -1);
            var panned = new PixelAxis(window.Shift(boundary), Left, 300);

            var groups = new HashSet<(long, long)>();
            for (int pixel = 0; pixel < axis.Width; pixel++)
            {
                var (start, end) = axis.ColumnsAt(pixel);
                groups.Add((start, end));
            }

            for (int pixel = 0; pixel < panned.Width; pixel++)
            {
                var (start, end) = panned.ColumnsAt(pixel);
                if (start + boundary < window.Columns && end + boundary <= window.Columns)
                    Assert.Contains((start + boundary, end + boundary), groups);
            }
        }

        [Fact]
        public void CombinedPositionAt_StandsForThePixelByTheMiddleOfItsColumns()
        {
            var axis = new PixelAxis(new DisplayWindow(0, 3, 1000), Left, 100);

            Assert.Equal(15, axis.PositionAt(Left + 0.5f));
            Assert.Equal(3 * 420 + 15, axis.PositionAt(Left + 42.5f));
        }

        [Fact]
        public void SpreadColumns_EachCoverAnEqualRunOfPixels()
        {
            var axis = new PixelAxis(new DisplayWindow(0, 7, 50), Left, 200);

            Assert.False(axis.Combines);
            Assert.True(axis.SingleColumnCells);
            Assert.Equal(7, axis.CellSpan);
            Assert.Equal(7, axis.WidestCell);
            for (int column = 0; column < 50; column++)
            {
                Assert.Equal(4 * column, axis.PixelOf(column));
                Assert.Equal(Left + 4 * column, axis.ColumnX(column));
            }
        }

        // NB: a lone column is drawn at its first sample, so that a cursor's line and time sit where the sample is.
        [Fact]
        public void SpreadPositionAt_StandsForEachOfAColumnsPixelsByItsFirstSample()
        {
            var axis = new PixelAxis(new DisplayWindow(0, 7, 50), Left, 200);

            for (int column = 0; column < 50; column++)
            {
                for (int pixel = 4 * column; pixel < 4 * column + 4; pixel++)
                    Assert.Equal(7 * column, axis.PositionAt(Left + pixel + 0.5f));
            }
        }

        [Theory]
        [InlineData(1000, 100)]
        [InlineData(1000, 300)]
        public void CombinedPixelAt_AndPositionAt_RoundTripThroughEveryPixel(int columns, int width)
        {
            var axis = new PixelAxis(new DisplayWindow(0, 3, columns), Left, width);

            for (int pixel = 0; pixel < width; pixel++)
            {
                var position = axis.PositionAt(Left + pixel + 0.5f);
                Assert.Equal(pixel, axis.PixelAt(position));
                Assert.Equal(Left + pixel, axis.X(position));
            }
        }

        [Fact]
        public void PositionAt_IsClampedToThePlot()
        {
            var axis = new PixelAxis(new DisplayWindow(0, 3, 1000), Left, 100);

            Assert.Equal(axis.PositionAt(Left + 0.5f), axis.PositionAt(Left - 50));
            Assert.Equal(axis.PositionAt(Left + 99.5f), axis.PositionAt(Left + 500));
        }

        [Theory]
        [InlineData(1000, 300)]
        [InlineData(50, 200)]
        public void Snap_GivesThePositionThatStandsForTheCell_AndIsStable(int columns, int width)
        {
            var axis = new PixelAxis(new DisplayWindow(0, 3, columns), Left, width);

            for (long position = 0; position < 3 * columns; position += 5)
            {
                var snapped = axis.Snap(position);
                Assert.Equal(axis.CellColumns(position), axis.CellColumns(snapped));
                Assert.Equal(snapped, axis.Snap(snapped));
            }
        }

        [Fact]
        public void Boundary_RoundsDownUpOrToTheNearerCellBoundary()
        {
            var axis = new PixelAxis(new DisplayWindow(0, 1, 1000), Left, 100);

            Assert.Equal(40, axis.Boundary(43, -1));
            Assert.Equal(50, axis.Boundary(43, 1));
            Assert.Equal(40, axis.Boundary(43, 0));
            Assert.Equal(50, axis.Boundary(47, 0));
            Assert.Equal(40, axis.Boundary(40, 1));
        }

        [Fact]
        public void ZeroWidth_PutsEverythingAtTheMiddleOfTheWindow()
        {
            var axis = new PixelAxis(new DisplayWindow(100, 3, 1000), Left, 0);

            Assert.False(axis.Combines);
            Assert.Equal(0, axis.PixelAt(1234));
            Assert.Equal(100 + 1500, axis.PositionAt(Left + 5));
        }
    }
}
