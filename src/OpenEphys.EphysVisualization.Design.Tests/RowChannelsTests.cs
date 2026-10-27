using System;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    [Collection(ImGuiCollection.Name)]
    public sealed class RowChannelsTests : IDisposable
    {
        const int Channels = 16;
        const int SampleRate = 1000;

        // NB: reversed, so that no row shows the channel of its own index.
        static readonly int[] Reversed = Enumerable.Range(0, Channels).Reverse().ToArray();

        readonly ImGuiHarness imgui = new();
        readonly LfpViewer settings = new() { Timebase = 1 };
        readonly ImGuiWaveformPanel panel;
        readonly Action draw;

        public RowChannelsTests()
        {
            panel = new ImGuiWaveformPanel(settings, 12 * SampleRate * Channels * sizeof(float), maxColumns: 2000);
            draw = panel.Draw;
            Feed();
        }

        public void Dispose()
        {
            panel.Dispose();
            imgui.Dispose();
        }

        [Fact]
        public void AHiddenChannel_IsHiddenInTheRowThatShowsIt()
        {
            settings.HiddenChannels = new[] { 3 };
            Reorder(Reversed);

            Assert.Equal(new[] { Channels - 1 - 3 }, HiddenRows());
        }

        [Fact]
        public void AShiftDragOverTheLabels_HidesTheChannelsOfTheRowsItCrosses()
        {
            Reorder(Reversed);
            var rows = panel.Rows();
            var x = panel.Axis().Left - 4;

            imgui.Hold(ImGuiKey.ModShift, true, draw);
            imgui.MoveMouse(new Vector2(x, RowMiddle(rows, 1)), draw);
            imgui.Button(ImGuiMouseButton.Left, true, draw);
            imgui.MoveMouse(new Vector2(x, RowMiddle(rows, 3)), draw);
            imgui.Button(ImGuiMouseButton.Left, false, draw);
            imgui.Hold(ImGuiKey.ModShift, false, draw);

            Assert.Equal(new[] { 12, 13, 14 }, settings.HiddenChannels);
        }

        [Fact]
        public void ANewOrder_KeepsTheSelectedChannelSelected()
        {
            var axis = panel.Axis();
            imgui.MoveMouse(new Vector2(axis.Left + axis.Width / 2, RowMiddle(panel.Rows(), 2)), draw);
            imgui.Click(ImGuiMouseButton.Left, draw);
            Assert.Equal(2, Private.Get<int>(panel, "selectedChannel"));

            Reorder(Reversed);

            Assert.Equal(Channels - 1 - 2, Private.Get<int>(panel, "selectedChannel"));
        }

        [Fact]
        public void ANewOrder_DiscardsTheBuffers_AndTheSameOrderAgainKeepsThem()
        {
            Reorder(Reversed);
            var decimator = Private.Get<Decimator>(panel, "decimator");

            panel.RowChannels = (int[])Reversed.Clone();
            Assert.Same(decimator, Private.Get<Decimator?>(panel, "decimator"));

            panel.RowChannels = null;
            Assert.Null(Private.Get<Decimator?>(panel, "decimator"));
        }

        void Reorder(int[] rowChannels)
        {
            panel.RowChannels = rowChannels;
            Feed();
        }

        void Feed()
        {
            TestSignal.Feed(TestSignal.Random(Channels, 5 * SampleRate), new[] { 100 }, block => panel.Update(block, SampleRate));
            imgui.Frames(3, draw);
        }

        int[] HiddenRows()
        {
            var hidden = Private.Get<bool[]>(panel, "ChannelHidden");
            return Enumerable.Range(0, hidden.Length).Where(row => hidden[row]).ToArray();
        }

        static float RowMiddle(in ImGuiWaveformPanel.RowLayout rows, int row) =>
            rows.RowTop(row) + rows.RowHeight / 2;
    }
}
