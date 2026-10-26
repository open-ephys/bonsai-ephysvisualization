using System;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class DisplayWindowTests
    {
        [Fact]
        public void Span_AndEnd_FollowFromTheStartStepAndColumns()
        {
            var window = new DisplayWindow(100, 7, 40);

            Assert.Equal(280, window.Span);
            Assert.Equal(380, window.End);
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(1, 107)]
        [InlineData(39, 373)]
        public void PositionOf_GivesWhereAColumnBegins(int column, long position)
        {
            Assert.Equal(position, new DisplayWindow(100, 7, 40).PositionOf(column));
        }

        [Theory]
        [InlineData(100, 0)]
        [InlineData(106, 0)]
        [InlineData(107, 1)]
        [InlineData(379, 39)]
        [InlineData(380, 40)]
        [InlineData(99, -1)]
        [InlineData(93, -1)]
        [InlineData(92, -2)]
        public void ColumnOf_FloorsTowardTheLeft_OnAndOffThePlot(long position, int column)
        {
            Assert.Equal(column, new DisplayWindow(100, 7, 40).ColumnOf(position));
        }

        [Theory]
        [InlineData(100, 0)]
        [InlineData(240, 0.5)]
        [InlineData(380, 1)]
        public void FractionOf_IsZeroAtTheLeftEdgeAndOneAtTheRight(double position, double fraction)
        {
            Assert.Equal(fraction, new DisplayWindow(100, 7, 40).FractionOf(position), 12);
        }

        [Fact]
        public void Shift_MovesTheStartAlone()
        {
            var shifted = new DisplayWindow(100, 7, 40).Shift(-30);

            Assert.True(shifted.Matches(new DisplayWindow(70, 7, 40)));
        }

        [Theory]
        [InlineData(3, 40, 250, 0.5)]
        [InlineData(13, 40, 250, 0.25)]
        [InlineData(1, 300, 101, 0)]
        [InlineData(20, 17, 379, 1)]
        public void Rescale_KeepsTheAnchorAtItsFraction_ToWithinAPosition(int step, int columns, long anchor, double fraction)
        {
            var rescaled = new DisplayWindow(100, 7, 40).Rescale(step, columns, anchor, fraction);

            Assert.Equal(step, rescaled.Step);
            Assert.Equal(columns, rescaled.Columns);
            // NB: the start is a whole position, so the anchor can fall up to one position short of its fraction.
            Assert.InRange(fraction - rescaled.FractionOf(anchor), -1e-12, 1.0 / rescaled.Span);
        }

        [Theory]
        [InlineData(100, 0, 1000, 100)]
        [InlineData(-50, 0, 1000, 0)]
        [InlineData(900, 0, 1000, 720)]
        [InlineData(100, 500, 600, 500)]
        public void Clamp_MovesAsLittleAsPossible_OrToTheFirstWhenTooWide(long start, long first, long last, long clamped)
        {
            Assert.Equal(clamped, new DisplayWindow(start, 7, 40).Clamp(first, last).Start);
        }

        [Fact]
        public void Matches_ComparesStartStepAndColumns()
        {
            var window = new DisplayWindow(100, 7, 40);

            Assert.True(window.Matches(new DisplayWindow(100, 7, 40)));
            Assert.False(window.Matches(new DisplayWindow(101, 7, 40)));
            Assert.False(window.Matches(new DisplayWindow(100, 8, 40)));
            Assert.False(window.Matches(new DisplayWindow(100, 7, 41)));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 0)]
        public void Constructor_RejectsAnEmptyStepOrColumnCount(int step, int columns)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayWindow(0, step, columns));
        }
    }
}
