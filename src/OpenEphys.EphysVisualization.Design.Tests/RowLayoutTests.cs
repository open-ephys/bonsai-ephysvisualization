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
                var channel = Scrolled.ChannelAt(y);
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
            Assert.Equal(5, layout.ChannelAt(339.5f));
            Assert.Equal(480, layout.Height);
        }
    }
}
