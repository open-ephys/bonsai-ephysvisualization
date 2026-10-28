using Xunit;
using RowLayout = OpenEphys.EphysVisualization.Design.ImGuiWaveformPanel.RowLayout;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class RowLayoutTests
    {
        // NB: a fitted height is a fraction of a pixel, and a scrolled table puts the first row above the plot.
        static readonly RowLayout Scrolled = new(0, 384, 2.6f, 100, 600, 100 - 37.3f);

        [Fact]
        public void ChannelAt_FindsTheRowEachPixelRowFallsIn()
        {
            for (float y = Scrolled.Top + 0.5f; y < Scrolled.Bottom; y++)
            {
                var channel = Scrolled.RowAt(y);
                Assert.InRange(y, Scrolled.RowTop(channel), Scrolled.RowTop(channel) + Scrolled.RowHeight);
            }
        }

        [Fact]
        public void VisibleRows_AreExactlyThoseThePlotCrosses()
        {
            var first = Scrolled.FirstVisible;
            var last = Scrolled.LastVisible;

            Assert.InRange(Scrolled.Top, Scrolled.RowTop(first), Scrolled.RowTop(first) + Scrolled.RowHeight);
            Assert.True(Scrolled.RowTop(last - 1) < Scrolled.Bottom);
            Assert.True(Scrolled.RowTop(last) >= Scrolled.Bottom);
        }

        [Fact]
        public void VisibleRows_StopAtTheLastRow_WhenTheRowsEndAboveThePlotsBottom()
        {
            var layout = new RowLayout(0, 10, 20, 100, 600, 100);

            Assert.Equal(0, layout.FirstVisible);
            Assert.Equal(10, layout.LastVisible);
            Assert.Equal(200, layout.Height);
        }

        [Fact]
        public void AnExpandedChannel_IsTheOnlyRow_AndFillsThePlot()
        {
            var layout = new RowLayout(5, 6, 480, 100, 580, 100);

            Assert.Equal(5, layout.FirstVisible);
            Assert.Equal(6, layout.LastVisible);
            Assert.Equal(100, layout.RowTop(5));
            Assert.Equal(5, layout.RowAt(339.5f));
            Assert.Equal(480, layout.Height);
        }

        // NB: rows 0 and 1 share slot 0, and slots 2 to 4 are empty.
        static readonly RowLayout Positioned = new(0, 4, 20, 100, 600, 100, new[] { 0f, 0, 1, 5 });

        [Fact]
        public void PositionedRows_LieAtTheirSlots_AndSpanThemAll()
        {
            Assert.Equal(100, Positioned.RowTop(1));
            Assert.Equal(120, Positioned.RowTop(2));
            Assert.Equal(200, Positioned.RowTop(3));
            Assert.Equal(120, Positioned.Height);
        }

        [Fact]
        public void PositionedRows_AreFoundWhereTheyLie_TheLastWhereTheyShare_AndNoneInAGap()
        {
            Assert.Equal(1, Positioned.RowAt(110));
            Assert.Equal(2, Positioned.RowAt(130));
            Assert.Equal(-1, Positioned.RowAt(150));
            Assert.Equal(3, Positioned.RowAt(210));
            Assert.Equal(4, Positioned.RowAt(230));
        }

        [Fact]
        public void VisiblePositionedRows_AreThoseThePlotCrosses_PastTheGaps()
        {
            var scrolled = new RowLayout(0, 4, 20, 100, 150, 100 - 30, new[] { 0f, 0, 1, 5 });

            Assert.Equal(2, scrolled.FirstVisible);
            Assert.Equal(3, scrolled.LastVisible);
        }
    }
}
