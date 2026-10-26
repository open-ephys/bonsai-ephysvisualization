using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    /// <summary>
    /// What the panel puts in ImGui's draw lists, where a 1 px line drawn off whole pixels blurs.
    /// </summary>
    [Collection(ImGuiCollection.Name)]
    public sealed class PanelDrawingTests : IDisposable
    {
        const int Channels = 16;
        const int SampleRate = 1000;

        readonly ImGuiHarness imgui = new();
        readonly LfpViewer settings = new() { Timebase = 1 };
        readonly ImGuiWaveformPanel panel;
        readonly Action draw;

        public PanelDrawingTests()
        {
            // NB: padding off whole pixels, which puts the plot's edges off them, as display scaling does.
            ImGui.GetStyle().FramePadding = new Vector2(4.3f, 3.3f);
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
        public void ThePlotFallsOffWholePixels()
        {
            Assert.NotEqual(0, panel.Rows().Top % 1);
            Assert.NotEqual(0, panel.Rows().RowHeight % 1);
        }

        [Fact]
        public void TheFrameAndDivisions_LieOnWholePixels()
        {
            AssertWholePixels(Rects(ImGuiWaveformPanel.FrameColor), "frame");
            var time = Rects(Color("ColDivision"));
            AssertWholePixels(time, "time divisions");

            imgui.Key(ImGuiKey.X, draw);
            var both = Rects(Color("ColDivision"));
            Assert.True(both.Count > time.Count, "An expanded channel has no amplitude divisions.");
            AssertWholePixels(both, "amplitude divisions");
        }

        [Fact]
        public void TheSelectedRowsBand_CoversThePixelRowsOfItsRow()
        {
            var rows = panel.Rows();
            var channel = 5;
            imgui.MoveMouse(new Vector2(panel.Axis().Left + 10, rows.RowTop(channel) + rows.RowHeight / 2), draw);
            imgui.Click(ImGuiMouseButton.Left, draw);
            imgui.MoveMouse(Vector2.Zero, draw);

            var band = Assert.Single(Rects(Color("ColSelectedRow")));
            Assert.Equal(MathF.Floor(rows.RowTop(channel)), band.Min.Y);
            Assert.Equal(MathF.Floor(rows.RowTop(channel + 1)), band.Max.Y);
        }

        [Fact]
        public void AZoomBoxTooNarrowToZoomTime_LiesOnWholePixels_AndSpansThePlotsWidth()
        {
            var (box, axis) = HeldBox(dx: 0, dy: 100);

            AssertWholePixels(new[] { box }, "zoom box");
            Assert.Equal(MathF.Floor(axis.Left), box.Min.X);
            Assert.Equal(MathF.Floor(axis.Left + axis.Width), box.Max.X);
        }

        [Fact]
        public void AZoomBoxTooShortToZoomTheChannels_LiesOnWholePixels_AndSpansThePlotsHeight()
        {
            var (box, _) = HeldBox(dx: 200, dy: 0);

            AssertWholePixels(new[] { box }, "zoom box");
            Assert.Equal(MathF.Floor(panel.Rows().Top), box.Min.Y);
            Assert.Equal(MathF.Floor(panel.Rows().Bottom), box.Max.Y);
        }

        (Rect Box, PixelAxis Axis) HeldBox(float dx, float dy)
        {
            imgui.Key(ImGuiKey.Space, draw);
            var axis = panel.Axis();
            var from = new Vector2(axis.Left + axis.Width / 4, ImGuiHarness.DisplaySize.Y / 3);
            imgui.MoveMouse(from, draw);
            imgui.Button(ImGuiMouseButton.Right, true, draw);
            imgui.MoveMouse(from + new Vector2(dx, dy), draw);
            return (Assert.Single(Rects(Color("ColZoomBoxFill"))), axis);
        }

        static void AssertWholePixels(IReadOnlyList<Rect> rects, string what)
        {
            Assert.NotEmpty(rects);
            foreach (var r in rects)
            {
                Assert.True(r.Min.X % 1 == 0 && r.Min.Y % 1 == 0 && r.Max.X % 1 == 0 && r.Max.Y % 1 == 0,
                    $"The {what} has a rectangle from {r.Min} to {r.Max}, off whole pixels.");
            }
        }

        static uint Color(string name) => Private.Get<uint>(typeof(ImGuiWaveformPanel), name);

        static List<Rect> Rects(uint color) => ImGuiHarness.Rects(color);
    }
}
