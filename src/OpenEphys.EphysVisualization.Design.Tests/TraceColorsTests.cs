using System;
using Hexa.NET.ImGui;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    [Collection(ImGuiCollection.Name)]
    public sealed class TraceColorsTests : IDisposable
    {
        readonly ImGuiHarness imgui = new();
        readonly WaveformScope settings = new();
        readonly TraceColors colors;

        public TraceColorsTests() => colors = new TraceColors(settings);

        public void Dispose() => imgui.Dispose();

        [Fact]
        public void AChannelTakesItsGroupsColor_WhateverThePalette_AndOneInNoGroupIsGrey()
        {
            colors.GroupOf = channel => channel == 5 ? 3 : -1;

            Assert.Equal(ImGuiPalette.Categorical[3], ImGui.ColorConvertFloat4ToU32(colors.Of(0, 5)));
            Assert.Equal(ImGuiPalette.Grey0xCC, ImGui.ColorConvertFloat4ToU32(colors.Of(1, 6)));

            settings.Palette = ColorPalette.Custom;
            Assert.Equal(ImGuiPalette.Categorical[3], ImGui.ColorConvertFloat4ToU32(colors.Of(0, 5)));
        }

        [Fact]
        public void WithoutGroups_EachRowTakesThePalettesNextColor()
        {
            Assert.Equal(ImGuiPalette.OpenEphysGuiLfp[1], ImGui.ColorConvertFloat4ToU32(colors.Of(1, 5)));

            settings.Palette = ColorPalette.Glasbey;
            Assert.Equal(ImGuiPalette.Categorical[1], ImGui.ColorConvertFloat4ToU32(colors.Of(1, 5)));
        }

        [Fact]
        public void TheCustomPalette_DrawsEveryRowInItsColor()
        {
            settings.Palette = ColorPalette.Custom;
            settings.CustomColor = System.Drawing.Color.FromArgb(0x12, 0x34, 0x56);

            Assert.All(new[] { 0, 1, 7 }, row =>
                Assert.Equal(0xFF563412u, ImGui.ColorConvertFloat4ToU32(colors.Of(row, row))));
        }
    }
}
