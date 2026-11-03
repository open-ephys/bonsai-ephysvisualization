using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class ControlStripGridTests
    {
        const float Gap = 20;

        [Fact]
        public void EveryColumn_GoesOnOneRow_WhereTheyAllFit()
        {
            var grid = ImGuiWaveformControlStrip.GridColumns(new float[] { 100, 55, 150 }, 345, Gap);

            Assert.Equal(new float[] { 100, 55, 150 }, grid);
        }

        [Fact]
        public void APlaceInTheGrid_IsAsWideAsTheWidestColumnInIt()
        {
            var grid = ImGuiWaveformControlStrip.GridColumns(new float[] { 100, 55, 150, 55, 130 }, 300, Gap);

            Assert.Equal(new float[] { 150, 55 }, grid);
        }

        [Fact]
        public void ARow_TakesFewerColumns_WhereTheirWidestWouldNotFit()
        {
            // NB: four to a row needs 150 + 55 + 55 + 150 and three gaps, 470.
            var widths = new float[] { 150, 55, 55, 150, 55, 55 };

            Assert.Equal(4, ImGuiWaveformControlStrip.GridColumns(widths, 470, Gap).Length);
            Assert.Equal(3, ImGuiWaveformControlStrip.GridColumns(widths, 469, Gap).Length);
        }

        [Fact]
        public void OneColumnARow_IsTheLeast_HoweverNarrowTheStrip()
        {
            var grid = ImGuiWaveformControlStrip.GridColumns(new float[] { 100, 150 }, 50, Gap);

            Assert.Equal(new float[] { 150 }, grid);
        }
    }
}
