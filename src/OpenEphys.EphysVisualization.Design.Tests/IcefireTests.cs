using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class IcefireTests
    {
        static readonly uint[] Map = Icefire.Map;
        static readonly int Middle = Map.Length / 2;

        [Fact]
        public void TheTwoHalvesMeetInBlack()
        {
            Assert.Equal(0, Map.Length % 2);
            Assert.Equal(0xFF000000u, Map[Middle - 1]);
            Assert.Equal(0xFF000000u, Map[Middle]);
        }

        [Fact]
        public void EveryEntryIsOpaque()
        {
            foreach (var color in Map)
                Assert.Equal(0xFFu, color >> 24);
        }

        [Fact]
        public void TheLowEndIsBlue_AndTheHighEndIsYellow()
        {
            var (r0, g0, b0) = Rgb(Map[0]);
            var (r1, g1, b1) = Rgb(Map[Map.Length - 1]);

            Assert.True(b0 > r0);
            Assert.True(r1 > b1 && g1 > b1);
        }

        // NB: the heatmap shows a larger magnitude by a brighter color, whichever its sign.
        [Fact]
        public void EachHalfBrightensAwayFromTheMiddle()
        {
            for (int i = Middle + 1; i < Map.Length; i++)
                Assert.True(Brightness(Map[i]) >= Brightness(Map[i - 1]), $"entry {i}");

            for (int i = Middle - 2; i >= 0; i--)
                Assert.True(Brightness(Map[i]) >= Brightness(Map[i + 1]), $"entry {i}");
        }

        static (int R, int G, int B) Rgb(uint color) =>
            ((int)(color & 0xFF), (int)(color >> 8 & 0xFF), (int)(color >> 16 & 0xFF));

        static int Brightness(uint color)
        {
            var (r, g, b) = Rgb(color);
            return r + g + b;
        }
    }
}
