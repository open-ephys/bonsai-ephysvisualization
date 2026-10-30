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
        readonly WaveformScope settings = new() { Timebase = 1 };
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
            settings.Channels = new[] { new ChannelSettings { Index = 3, Hidden = true } };
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

            var channels = Assert.IsType<ChannelSettings[]>(settings.Channels);
            Assert.Equal(new[] { 12, 13, 14 }, channels.Select(channel => channel.Index));
            Assert.All(channels, channel => Assert.True(channel.Hidden));
        }

        [Fact]
        public void ShowingAHiddenChannel_KeepsItsGroup_AndDropsAChannelLeftAtItsDefaults()
        {
            settings.Channels = new[]
            {
                new ChannelSettings { Index = 2, Hidden = true, Group = 4 },
                new ChannelSettings { Index = 7, Hidden = true },
            };

            var shown = ChannelSettings.WithHidden(settings.Channels, Array.Empty<int>());

            var only = Assert.Single(shown!);
            Assert.Equal((2, false, 4), (only.Index, only.Hidden, only.Group));
            Assert.Null(ChannelSettings.WithHidden(new[] { new ChannelSettings { Index = 7, Hidden = true } },
                Array.Empty<int>()));
        }

        [Fact]
        public void ANewOrder_KeepsTheSelectedChannelSelected()
        {
            var axis = panel.Axis();
            imgui.MoveMouse(new Vector2(axis.Left + axis.Width / 2, RowMiddle(panel.Rows(), 2)), draw);
            imgui.Click(ImGuiMouseButton.Left, draw);
            Assert.Equal(2, Private.Get<int>(panel, "selectedRow"));

            Reorder(Reversed);

            Assert.Equal(Channels - 1 - 2, Private.Get<int>(panel, "selectedRow"));
        }

        [Fact]
        public void ANewOrder_DiscardsTheBuffers_AndTheSameOrderAgainKeepsThem()
        {
            Reorder(Reversed);
            var decimator = Private.Get<Decimator>(panel, "decimator");

            panel.Order = new RowOrder((int[])Reversed.Clone());
            Assert.Same(decimator, Private.Get<Decimator?>(panel, "decimator"));

            panel.Order = null;
            Assert.Null(Private.Get<Decimator?>(panel, "decimator"));
        }

        [Fact]
        public void NewPositions_KeepTheBuffers_AndFittingFitsTheRowsWithTheirGaps()
        {
            Reorder(Reversed);
            var decimator = Private.Get<Decimator>(panel, "decimator");

            // NB: the last row is 2.5 pitches below the one before it, leaving a 1.5 pitch gap.
            var positions = Enumerable.Range(0, Channels).Select(row => 10f * row).ToArray();
            positions[Channels - 1] += 15;
            panel.Order = new RowOrder((int[])Reversed.Clone(), positions);
            imgui.Frames(3, draw);

            Assert.Same(decimator, Private.Get<Decimator?>(panel, "decimator"));
            var rows = panel.Rows();
            Assert.Equal(rows.RowTop(Channels - 2) + 2.5f * rows.RowHeight, rows.RowTop(Channels - 1), 2);
            Assert.Equal(Private.Get<float>(panel, "visibleHeight"), rows.Height, 1);
        }

        [Fact]
        public void Positions_MustBeOnePerChannel_AndNeverFall()
        {
            Assert.Throws<ArgumentException>(() => panel.Order = new RowOrder(Reversed, new float[Channels - 1]));

            var falling = Enumerable.Range(0, Channels).Select(row => -1f * row).ToArray();
            Assert.Throws<ArgumentException>(() => panel.Order = new RowOrder(Reversed, falling));
        }

        // NB: the first half of the rows in one lane and the second half in another.
        static readonly int[] TwoLanes = Enumerable.Range(0, Channels).Select(row => row < Channels / 2 ? 0 : 1).ToArray();

        [Fact]
        public void TheRowsOfASecondLane_LieLevelWithTheFirsts_AndThePointerFindsThemThere()
        {
            Reorder(Reversed);
            panel.Order = new RowOrder((int[])Reversed.Clone(), Lanes: TwoLanes);
            imgui.Frames(3, draw);
            var columnLeft = Private.Get<float>(panel, "plotColumnLeft");
            var columnWidth = Private.Get<float>(panel, "plotColumnWidth");
            var first = panel.Rows();

            imgui.MoveMouse(new Vector2(columnLeft + columnWidth - 10, RowMiddle(first, 0)), draw);
            imgui.Frames(2, draw);

            var second = panel.Rows();
            Assert.Equal(Channels / 2, second.FirstRow);
            Assert.Equal(first.RowTop(0), second.RowTop(Channels / 2));
            Assert.True(panel.Axis().Left > columnLeft + columnWidth / 2);
        }

        [Fact]
        public void TheLaneButton_WidensItsLane_AndAgainGivesTheLanesEqualWidths()
        {
            Reorder(Reversed);
            panel.Order = new RowOrder((int[])Reversed.Clone(), Lanes: TwoLanes);
            imgui.Frames(3, draw);
            var equal = panel.Axis().Width;
            var button = LaneButton();

            imgui.MoveMouse(button, draw);
            imgui.Click(ImGuiMouseButton.Left, draw);
            imgui.Frames(2, draw);
            Assert.Equal(0, Private.Get<int>(panel, "expandedLane"));
            Assert.True(panel.Axis().Width > equal);

            imgui.MoveMouse(LaneButton(widened: true), draw);
            imgui.Click(ImGuiMouseButton.Left, draw);
            imgui.Frames(2, draw);
            Assert.Equal(-1, Private.Get<int>(panel, "expandedLane"));
            Assert.Equal(equal, panel.Axis().Width);
        }

        [Fact]
        public void L_TogglesTheLaneUnderThePointer_OrElseTheSelectedChannels()
        {
            Reorder(Reversed);
            panel.Order = new RowOrder((int[])Reversed.Clone(), Lanes: TwoLanes);
            imgui.Frames(3, draw);
            var rows = panel.Rows();

            imgui.MoveMouse(new Vector2(panel.Axis().Left + 10, RowMiddle(rows, 2)), draw);
            imgui.Key(ImGuiKey.L, draw);
            Assert.Equal(0, Private.Get<int>(panel, "expandedLane"));
            imgui.Key(ImGuiKey.L, draw);
            Assert.Equal(-1, Private.Get<int>(panel, "expandedLane"));

            // NB: channel 3 is in the second lane.
            panel.SelectedChannel = 3;
            imgui.MoveMouse(new Vector2(-100, -100), draw);
            imgui.Key(ImGuiKey.L, draw);
            Assert.Equal(1, Private.Get<int>(panel, "expandedLane"));
        }

        [Fact]
        public void RevealingAChannelInAStrip_WidensItsLane()
        {
            Reorder(Reversed);
            panel.Order = new RowOrder((int[])Reversed.Clone(), Lanes: TwoLanes);
            imgui.Frames(3, draw);
            imgui.MoveMouse(LaneButton(), draw);
            imgui.Click(ImGuiMouseButton.Left, draw);
            imgui.Frames(2, draw);
            Assert.Equal(0, Private.Get<int>(panel, "expandedLane"));

            // NB: channel 0 is in the last row, in the second lane.
            panel.Reveal(0);
            imgui.Frames(2, draw);

            Assert.Equal(1, Private.Get<int>(panel, "expandedLane"));
        }

        [Fact]
        public void TheSelectedChannel_IsSetByChannel_AndAHiddenOneSelectsNone()
        {
            Reorder(Reversed);

            panel.SelectedChannel = 3;
            Assert.Equal(Channels - 1 - 3, Private.Get<int>(panel, "selectedRow"));
            Assert.Equal(3, panel.SelectedChannel);

            settings.Channels = new[] { new ChannelSettings { Index = 5, Hidden = true } };
            panel.SelectedChannel = 5;
            Assert.Equal(-1, panel.SelectedChannel);
        }

        [Fact]
        public void ZoomingToChannels_FitsTheRowsFromTheHighestToTheLowest()
        {
            Reorder(Reversed);
            var visible = Private.Get<float>(panel, "visibleHeight");

            // NB: channels 5 and 3 are in rows 10 and 12.
            panel.ZoomTo(new[] { 5, 3 });
            imgui.Frames(2, draw);

            Assert.False(panel.FitChannels);
            Assert.Equal(MathF.Floor(visible / 3), panel.ChannelHeight);
            var rows = panel.Rows();
            Assert.Equal(rows.Top, rows.RowTop(10), 1);
        }

        [Fact]
        public void ZoomingToChannelsInTwoLanes_WidensEveryLane()
        {
            Reorder(Reversed);
            panel.Order = new RowOrder((int[])Reversed.Clone(), Lanes: TwoLanes);
            imgui.Frames(3, draw);
            imgui.MoveMouse(LaneButton(), draw);
            imgui.Click(ImGuiMouseButton.Left, draw);
            imgui.Frames(2, draw);
            Assert.Equal(0, Private.Get<int>(panel, "expandedLane"));
            var visible = Private.Get<float>(panel, "visibleHeight");

            // NB: channel 13 is the third row of the first lane, and channel 3 the fifth of the second.
            panel.ZoomTo(new[] { 13, 3 });
            imgui.Frames(2, draw);

            Assert.Equal(-1, Private.Get<int>(panel, "expandedLane"));
            Assert.Equal(MathF.Floor(visible / 3), panel.ChannelHeight);
        }

        [Fact]
        public void AChannelHoveredElsewhere_IsHovered_ButTakesNoClick()
        {
            Reorder(Reversed);
            imgui.MoveMouse(new Vector2(-100, -100), draw);

            panel.HoveredChannel = 3;
            imgui.Frames(1, draw);
            Assert.Equal(3, panel.HoveredChannel);

            // NB: a double-click on a hovered row would expand it.
            imgui.Click(ImGuiMouseButton.Left, draw);
            imgui.Click(ImGuiMouseButton.Left, draw);
            Assert.Equal(-1, Private.Get<int>(panel, "expandedRow"));

            panel.HoveredChannel = -1;
            imgui.Frames(1, draw);
            Assert.Equal(-1, panel.HoveredChannel);
        }

        [Fact]
        public void Lanes_MustBeOnePerChannel_WithEachLanesRowsTogether()
        {
            Assert.Throws<ArgumentException>(() => panel.Order = new RowOrder(Reversed, Lanes: new int[Channels - 1]));

            var split = Enumerable.Range(0, Channels).Select(row => row % 2).ToArray();
            Assert.Throws<ArgumentException>(() => panel.Order = new RowOrder(Reversed, Lanes: split));
        }

        // NB: the first lane's button, in its plot's top right corner, left of the maximize button once the first
        // lane is the only one drawn.
        Vector2 LaneButton(bool widened = false)
        {
            var axis = panel.Axis();
            var side = ImGui.GetFrameHeight();
            var pad = ImGui.GetStyle().FramePadding;
            var right = axis.Left + axis.Width - (widened ? side + pad.X : 0);
            return new Vector2(right - pad.X - side / 2, panel.Rows().Top + pad.Y + side / 2);
        }

        void Reorder(int[] rowChannels)
        {
            panel.Order = new RowOrder(rowChannels);
            Feed();
        }

        void Feed()
        {
            TestSignal.Feed(TestSignal.Random(Channels, 5 * SampleRate), new[] { 100 }, block => panel.Update(block, SampleRate));
            imgui.Frames(3, draw);
        }

        int[] HiddenRows()
        {
            var hidden = Private.Get<bool[]>(panel, "RowHidden");
            return Enumerable.Range(0, hidden.Length).Where(row => hidden[row]).ToArray();
        }

        static float RowMiddle(in ImGuiWaveformPanel.RowLayout rows, int row) =>
            rows.RowTop(row) + rows.RowHeight / 2;
    }
}
