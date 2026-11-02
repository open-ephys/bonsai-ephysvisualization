using System.Collections.Generic;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class SpikeViewTests
    {
        const float Span = 1000;
        static readonly float Knee = ImGuiSpikeView.Knee(Span);

        [Fact]
        public void AnAmplitude_ComesBackFromItsBin()
        {
            foreach (var value in new[] { -900f, -120f, -3f, 0f, 0.5f, 40f, 750f })
            {
                var bin = ImGuiSpikeView.BinOf(value, Span, Knee);
                Assert.Equal(value, ImGuiSpikeView.ValueOf(bin, Span, Knee), value == 0 ? 1e-3 : System.Math.Abs(value) * 1e-4);
            }
        }

        [Fact]
        public void Bins_AreFineNearZero_AndCoarserFarFromIt_KeptToTheTile()
        {
            float Width(float value) =>
                ImGuiSpikeView.ValueOf(ImGuiSpikeView.BinOf(value, Span, Knee) + 1, Span, Knee) - value;

            Assert.True(Width(0) < 1, $"near zero {Width(0)}");
            Assert.True(Width(500) > 10 * Width(0), $"at 500 {Width(500)}");
            Assert.Equal(1024 / 2f, ImGuiSpikeView.BinOf(0, Span, Knee), 3);
            Assert.Equal(1024 - 0.5f, ImGuiSpikeView.BinOf(5 * Span, Span, Knee));
            Assert.Equal(0.5f, ImGuiSpikeView.BinOf(-5 * Span, Span, Knee));
        }

        [Fact]
        public void EachChannel_HasATileOfItsOwn_InRowsOfTheAtlas()
        {
            var width = ImGuiSpikeView.TileWidth(60);

            Assert.Equal((0f, 0f), ImGuiSpikeView.TileOrigin(0, 10, width));
            Assert.Equal((3f * width, 0f), ImGuiSpikeView.TileOrigin(3, 10, width));
            Assert.Equal((2f * width, 1024f), ImGuiSpikeView.TileOrigin(12, 10, width));
        }

        [Fact]
        public void AWaveform_IsDrawnAsLinesBetweenItsSamples_AcrossItsTile()
        {
            var points = new List<float>();
            var width = ImGuiSpikeView.TileWidth(4);

            ImGuiSpikeView.AppendSegments(new[] { 1f, 2f, 3f, 4f }, 4, 100, 512, points);

            Assert.Equal(3 * 2 * 3, points.Count);
            Assert.Equal(new[] { 100.5f, 512f, 1f, 100.5f + (width - 1) / 3f, 512f, 2f }, points.GetRange(0, 6));
            Assert.Equal(100.5f + width - 1, points[points.Count - 3]);
            Assert.Equal(4f, points[points.Count - 1]);
        }
    }
}
