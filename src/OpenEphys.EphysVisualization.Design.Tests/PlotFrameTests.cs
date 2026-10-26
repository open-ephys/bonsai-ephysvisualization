using Xunit;
using RowLayout = OpenEphys.EphysVisualization.Design.ImGuiWaveformPanel.RowLayout;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class PlotFrameTests
    {
        [Theory]
        [InlineData(0, 200)]
        [InlineData(50, 200)]
        [InlineData(-12.5, 25)]
        public void ARowSpansTheRange_AboutTheCenter(double center, double range)
        {
            using var envelope = new Envelope(4, 100);
            var frame = Frame(envelope, center, range);
            var top = frame.Layout.RowTop(2);

            Assert.Equal(top + 20, frame.ValueY(2, center), 3);
            Assert.Equal(top, frame.ValueY(2, center + range / 2), 3);
            Assert.Equal(top + 40, frame.ValueY(2, center - range / 2), 3);
        }

        [Theory]
        [InlineData(180.5f)]
        [InlineData(199)]
        [InlineData(219.75f)]
        public void ValueAt_IsTheInverseOfValueY(float y)
        {
            using var envelope = new Envelope(4, 100);
            var frame = Frame(envelope, center: 50, range: 200);

            Assert.Equal(y, frame.ValueY(2, frame.ValueAt(2, y)), 3);
        }

        static PlotFrame Frame(Envelope envelope, double center, double range) => new(
            envelope, new RowLayout(0, 4, 40, 100, 600, 100), new PixelAxis(new DisplayWindow(0, 1, 100), 0, 100),
            100, 600, 1000, false, 0, 1, 1, range, center, "µV", new bool[4], -1, -1, false, 0);
    }
}
