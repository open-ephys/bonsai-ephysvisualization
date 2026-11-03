using System;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    [Collection(ImGuiCollection.Name)]
    public sealed class PanelGestureTests : IDisposable
    {
        const int Channels = 16;
        const int SampleRate = 1000;

        readonly ImGuiHarness imgui = new();
        readonly WaveformScope settings = new() { Timebase = 1 };
        readonly ImGuiWaveformPanel panel;
        readonly Action draw;

        public PanelGestureTests()
        {
            panel = new ImGuiWaveformPanel(settings, 12 * SampleRate * Channels * sizeof(float), maxColumns: 2000);
            draw = panel.Draw;
            TestSignal.Feed(TestSignal.Random(Channels, 5 * SampleRate), new[] { 100 }, block => panel.Update(block, SampleRate));
            imgui.Frames(3, draw);
        }

        public void Dispose()
        {
            panel.Dispose();
            imgui.Dispose();
        }

        [Fact]
        public void ThePlotIsLaidOut_WithoutAGlContext()
        {
            Assert.True(panel.Axis().Width > 0);
        }

        [Fact]
        public void T_SetsTheLongestTimebase_WhichWhilePausedIsTheLongestTheRecordFills()
        {
            imgui.Key(ImGuiKey.T, draw);
            Assert.Equal(10, panel.Timebase, 12);

            panel.Paused = true;
            imgui.Frame(draw);
            imgui.Key(ImGuiKey.T, draw);
            Assert.Equal(5, panel.Timebase, 12);
        }

        [Fact]
        public void AWideRightDrag_WhilePaused_ZoomsTheTimebaseToTheBox()
        {
            panel.Paused = true;
            imgui.Frame(draw);
            var axis = panel.Axis();
            var y = ImGuiHarness.DisplaySize.Y / 2;
            var from = new Vector2(axis.Left + axis.Width / 4, y);
            var to = new Vector2(axis.Left + axis.Width / 2, y + 2);

            RightDrag(from, to);

            Assert.Equal(PlotText.Significant(1.0 * (to.X - from.X) / axis.Width), panel.Timebase, 12);
        }

        [Fact]
        public void ARightDrag_WhileLive_DoesNothing()
        {
            var axis = panel.Axis();
            var y = ImGuiHarness.DisplaySize.Y / 2;

            RightDrag(new Vector2(axis.Left + axis.Width / 4, y), new Vector2(axis.Left + axis.Width / 2, y + 100));

            Assert.Equal(1, panel.Timebase, 12);
        }

        [Fact]
        public void Escape_CancelsTheBoxBeingDragged()
        {
            panel.Paused = true;
            imgui.Frame(draw);
            var axis = panel.Axis();
            var y = ImGuiHarness.DisplaySize.Y / 2;

            imgui.MoveMouse(new Vector2(axis.Left + axis.Width / 4, y), draw);
            imgui.Button(ImGuiMouseButton.Right, true, draw);
            imgui.MoveMouse(new Vector2(axis.Left + axis.Width / 2, y + 2), draw);
            imgui.Key(ImGuiKey.Escape, draw);
            imgui.Button(ImGuiMouseButton.Right, false, draw);

            Assert.Equal(1, panel.Timebase, 12);
        }

        [Fact]
        public void ARightDoubleClick_ZoomsAllTheWayOut_FittingTheChannelsOnce()
        {
            settings.FitChannels = false;
            settings.ChannelHeight = 200;
            imgui.MoveMouse(new Vector2(panel.Axis().Left + panel.Axis().Width / 2, ImGuiHarness.DisplaySize.Y / 2), draw);

            imgui.Click(ImGuiMouseButton.Right, draw);
            imgui.Click(ImGuiMouseButton.Right, draw);
            imgui.Frames(2, draw);

            Assert.Equal(10, panel.Timebase, 12);
            Assert.False(panel.FitChannels);
            var rows = panel.Rows();
            Assert.Equal(0, rows.FirstVisible);
            Assert.True(rows.RowTop(rows.LastRow - 1) + rows.RowHeight <= rows.Bottom + 1,
                $"last row ends at {rows.RowTop(rows.LastRow - 1) + rows.RowHeight}, plot at {rows.Bottom}");
        }

        [Fact]
        public void ARightDoubleClick_LeavesFitOn()
        {
            settings.FitChannels = true;
            imgui.MoveMouse(new Vector2(panel.Axis().Left + panel.Axis().Width / 2, ImGuiHarness.DisplaySize.Y / 2), draw);

            imgui.Click(ImGuiMouseButton.Right, draw);
            imgui.Click(ImGuiMouseButton.Right, draw);

            Assert.True(panel.FitChannels);
        }

        [Fact]
        public void Keys_AreLeftToAPaneThatAnswersItsOwn()
        {
            settings.FitChannels = false;

            panel.KeysElsewhere = true;
            imgui.Key(ImGuiKey.F, draw);
            Assert.False(panel.FitChannels);

            panel.KeysElsewhere = false;
            imgui.Key(ImGuiKey.F, draw);
            Assert.True(panel.FitChannels);
        }

        [Fact]
        public void TheSelectionKeys_AreAnsweredOverAPane_WhichSelectsToo()
        {
            panel.SelectedChannel = 5;
            panel.KeysElsewhere = true;

            imgui.Key(ImGuiKey.X, draw);
            Assert.Equal(5, panel.Expanded?.Channel);

            imgui.Key(ImGuiKey.E, draw);
            imgui.Frame(draw);
            Assert.Equal(6, panel.Expanded?.Channel);
        }

        [Fact]
        public void ATallRightDrag_WhilePaused_FitsTheChannelsItSpansToThePlot()
        {
            panel.Paused = true;
            imgui.Frame(draw);
            var rows = panel.Rows();
            var x = panel.Axis().Left + panel.Axis().Width / 2;
            var visible = rows.Bottom - rows.Top;

            RightDrag(new Vector2(x, RowMiddle(rows, 4)), new Vector2(x, RowMiddle(rows, 7)));

            rows = panel.Rows();
            Assert.False(panel.FitChannels);
            Assert.True(4 * panel.ChannelHeight <= visible && 5 * panel.ChannelHeight > visible,
                $"height {panel.ChannelHeight}, visible {visible}");
            Assert.Equal(4, rows.FirstVisible);
            Assert.Equal(rows.Top, rows.RowTop(4), 1);
        }

        [Fact]
        public void ARightDrag_WhileLive_FitsTheChannelsItSpans_AndLeavesTheTimebase()
        {
            imgui.Frame(draw);
            var rows = panel.Rows();
            var timebase = panel.Timebase;
            var left = panel.Axis().Left + 10;
            var visible = rows.Bottom - rows.Top;

            RightDrag(new Vector2(left, RowMiddle(rows, 4)), new Vector2(left + panel.Axis().Width / 4, RowMiddle(rows, 7)));

            Assert.Equal(timebase, panel.Timebase, 12);
            Assert.True(4 * panel.ChannelHeight <= visible && 5 * panel.ChannelHeight > visible,
                $"height {panel.ChannelHeight}, visible {visible}");
            Assert.Equal(4, panel.Rows().FirstVisible);
        }

        [Fact]
        public void ATallRightDrag_OnAnExpandedChannel_ZoomsItsAmplitudeToTheBox()
        {
            panel.Paused = true;
            imgui.Frame(draw);
            imgui.Key(ImGuiKey.X, draw);
            var rows = panel.Rows();
            Assert.Equal(1, rows.LastRow - rows.FirstRow);
            var x = panel.Axis().Left + panel.Axis().Width / 2;
            var top = rows.RowTop(rows.FirstRow);
            // NB: whole pixels, which is where ImGui puts the pointer.
            var from = MathF.Floor(top + rows.RowHeight / 4);
            var to = MathF.Floor(top + rows.RowHeight / 2);
            var range = panel.Range;

            RightDrag(new Vector2(x, from), new Vector2(x, to));

            var (center, span) = panel.AmplitudeView();
            Assert.Equal(PlotText.Significant(range * (to - from) / rows.RowHeight), span, 9);
            Assert.Equal(range * (top + rows.RowHeight / 2 - (from + to) / 2) / rows.RowHeight, center, 4);
        }

        [Fact]
        public void Collapsing_ReturnsToTheScrollTheExpandBeganFrom()
        {
            panel.ChannelHeight = 100;
            Scroll(5);
            var origin = panel.Rows().Origin;

            imgui.Key(ImGuiKey.X, draw);
            imgui.Key(ImGuiKey.E, draw);
            Assert.Equal(6, panel.Rows().FirstRow);
            imgui.Key(ImGuiKey.X, draw);
            imgui.Frames(2, draw);

            Assert.Equal(origin, panel.Rows().Origin);
        }

        [Fact]
        public void Collapsing_AfterSteppingOutOfView_ScrollsOnlyAsFarAsBringsTheChannelIntoView()
        {
            panel.ChannelHeight = 100;
            Scroll(5);

            imgui.Key(ImGuiKey.X, draw);
            for (int i = 0; i < 9; i++)
                imgui.Key(ImGuiKey.E, draw);
            Assert.Equal(14, panel.Rows().FirstRow);
            imgui.Key(ImGuiKey.X, draw);
            imgui.Frames(2, draw);

            var rows = panel.Rows();
            Assert.Equal(rows.Bottom, rows.RowTop(14) + rows.RowHeight, 1);
        }

        [Fact]
        public void TheTracesAndTheHeatmap_KeepRangesOfTheirOwn()
        {
            settings.RangeAmplitude = 1000;
            settings.RangeHeatmap = 50;
            Assert.Equal(1000, panel.Range);

            imgui.Key(ImGuiKey.H, draw);
            Assert.Equal(50, panel.Range);
            imgui.Key(ImGuiKey.ModAlt, ImGuiKey.W, draw);
            Assert.Equal(25, settings.RangeHeatmap);

            imgui.Key(ImGuiKey.H, draw);
            Assert.Equal(1000, panel.Range);
            Assert.Equal(1000, settings.RangeAmplitude);
        }

        [Fact]
        public void AnExpandedChannel_InTheHeatmap_IsDrawnAtTheTracesRange()
        {
            settings.RangeAmplitude = 1000;
            settings.RangeHeatmap = 50;
            settings.ShowHeatmap = true;

            imgui.Key(ImGuiKey.X, draw);

            Assert.False(panel.HeatmapDrawn);
            Assert.Equal(1000, panel.Range);
            Assert.Equal(1000, panel.AmplitudeView().Span);
        }

        [Fact]
        public void TheColorThreshold_IsLimitedByTheHeatmapsRange()
        {
            settings.RangeAmplitude = 1000;
            settings.RangeHeatmap = 100;
            settings.ColorThreshold = 200;

            Assert.Equal(100, panel.ColorThreshold);
        }

        [Fact]
        public void TheRange_IsHeldToTheLargest_HoweverItIsSet()
        {
            panel.MaxRange = 5000;
            settings.RangeAmplitude = 5e27;
            Assert.Equal(5000, panel.Range);

            panel.Range = 1e6;
            Assert.Equal(5000, settings.RangeAmplitude);

            imgui.Key(ImGuiKey.ModAlt, ImGuiKey.S, draw);
            Assert.Equal(5000, settings.RangeAmplitude);
        }

        [Fact]
        public void TheRangesOffered_StopAtTheLargest()
        {
            settings.RangeAmplitude = 2500;
            panel.MaxRange = 5000;

            Assert.Equal(5000, panel.StandardRanges.Last());
        }

        [Fact]
        public void TheRange_IsHeldToTheSmallest_HoweverItIsSet()
        {
            panel.MinRange = 10;
            settings.RangeAmplitude = 1e-3;
            Assert.Equal(10, panel.Range);

            panel.Range = 1;
            Assert.Equal(10, settings.RangeAmplitude);

            imgui.Key(ImGuiKey.ModAlt, ImGuiKey.W, draw);
            Assert.Equal(10, settings.RangeAmplitude);
        }

        [Fact]
        public void TheRangesOffered_StartAtTheSmallest()
        {
            settings.RangeAmplitude = 25;
            panel.MinRange = 10;

            Assert.Equal(10, panel.StandardRanges.First());
        }

        [Fact]
        public void TheRangesOffered_BetweenTheSmallestAndTheLargest_AreAllOfThem_WhereverTheRangeIs()
        {
            panel.MinRange = 10;
            panel.MaxRange = 5000;
            var all = new double[] { 10, 25, 50, 100, 250, 500, 1000, 2500, 5000 };

            foreach (var range in new double[] { 10, 250, 5000 })
            {
                settings.RangeAmplitude = range;
                Assert.Equal(all, panel.StandardRanges);
            }
        }

        [Fact]
        public void TogglingExpanded_ExpandsTheSelection_ThenCollapsesIt_AsXDoes()
        {
            panel.SelectedChannel = 5;
            panel.ToggleExpanded();
            imgui.Frames(2, draw);
            Assert.Equal(5, panel.Expanded?.Channel);

            panel.ToggleExpanded();
            imgui.Frames(2, draw);
            Assert.Null(panel.Expanded);
        }

        void Scroll(int channels)
        {
            for (int i = 0; i < channels; i++)
                imgui.Key(ImGuiKey.S, draw);
            imgui.Frames(2, draw);
            Assert.Equal(panel.Rows().Top, panel.Rows().RowTop(channels), 1);
        }

        static float RowMiddle(in ImGuiWaveformPanel.RowLayout rows, int channel) =>
            rows.RowTop(channel) + rows.RowHeight / 2;

        void RightDrag(Vector2 from, Vector2 to)
        {
            imgui.MoveMouse(from, draw);
            imgui.Button(ImGuiMouseButton.Right, true, draw);
            imgui.MoveMouse(to, draw);
            imgui.Button(ImGuiMouseButton.Right, false, draw);
            imgui.Frames(2, draw);
        }
    }
}
