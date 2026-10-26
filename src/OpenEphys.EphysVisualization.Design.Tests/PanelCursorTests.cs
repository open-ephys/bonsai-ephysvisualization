using System;
using System.Numerics;
using Hexa.NET.ImGui;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    [Collection(ImGuiCollection.Name)]
    public sealed class PanelCursorTests : IDisposable
    {
        const int Channels = 4;
        const int SampleRate = 1000;

        readonly ImGuiHarness imgui = new();
        readonly LfpViewer settings = new() { Timebase = 1 };
        readonly ImGuiWaveformPanel panel;
        readonly Action draw;

        public PanelCursorTests()
        {
            panel = new ImGuiWaveformPanel(settings, 12 * SampleRate * Channels * sizeof(float), maxColumns: 2000);
            draw = panel.Draw;
            TestSignal.Feed(TestSignal.Random(Channels, 5 * SampleRate), new[] { 100 }, block => panel.Update(block, SampleRate));
            imgui.Frames(3, draw);
            imgui.Key(ImGuiKey.C, draw);
        }

        public void Dispose()
        {
            panel.Dispose();
            imgui.Dispose();
        }

        long Cursor => Assert.Single(panel.CursorPositions());

        long Middle
        {
            get
            {
                var window = panel.Axis().Window;
                return panel.Axis().Snap(window.Start + window.Span / 2);
            }
        }

        [Fact]
        public void ShowingTheCursors_PlacesOneInTheMiddleOfTheView()
        {
            Assert.Equal(Middle, Cursor);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        public void RightArrow_StepsTheCursorToTheNextCell_WhereverTheViewCombinesOrSpreadsTheSamples(double timebase)
        {
            settings.Timebase = timebase;
            imgui.Frames(2, draw);
            panel.HomeCursors();
            var axis = panel.Axis();
            var from = axis.CellColumns(Cursor);

            imgui.Key(ImGuiKey.RightArrow, draw);

            Assert.Equal(from.End, panel.Axis().CellColumns(Cursor).Start);
        }

        [Fact]
        public void ShiftArrow_StepsTenCells()
        {
            var start = Cursor;
            imgui.Key(ImGuiKey.ModShift, ImGuiKey.RightArrow, draw);
            var coarse = Cursor;

            imgui.Key(ImGuiKey.ModShift, ImGuiKey.LeftArrow, draw);
            Assert.Equal(start, Cursor);
            for (int i = 0; i < 10; i++)
                imgui.Key(ImGuiKey.RightArrow, draw);

            Assert.Equal(coarse, Cursor);
        }

        [Fact]
        public void TheArrows_StopTheCursorAtThePlotsEdges()
        {
            var cells = panel.Axis().Width;
            for (int i = 0; i < cells / 10 + 2; i++)
                imgui.Key(ImGuiKey.ModShift, ImGuiKey.RightArrow, draw);
            var axis = panel.Axis();
            Assert.InRange(axis.X(Cursor), axis.Left, axis.Left + axis.Width - 1);

            for (int i = 0; i < cells / 10 + 2; i++)
                imgui.Key(ImGuiKey.ModShift, ImGuiKey.LeftArrow, draw);
            Assert.InRange(axis.X(Cursor), axis.Left, axis.Left + axis.Width - 1);
        }

        [Fact]
        public void Home_BringsTheCursorBackToTheMiddle()
        {
            imgui.Key(ImGuiKey.ModShift, ImGuiKey.RightArrow, draw);
            Assert.NotEqual(Middle, Cursor);

            panel.HomeCursors();

            Assert.Equal(Middle, Cursor);
        }

        [Fact]
        public void DraggingACursorsLine_MovesItToThePointer()
        {
            var axis = panel.Axis();
            var y = ImGuiHarness.DisplaySize.Y / 2;
            var to = MathF.Floor(axis.Left + axis.Width / 4);

            imgui.MoveMouse(new Vector2(axis.X(Cursor), y), draw);
            imgui.Button(ImGuiMouseButton.Left, true, draw);
            imgui.MoveMouse(new Vector2(to, y), draw);
            imgui.Button(ImGuiMouseButton.Left, false, draw);

            Assert.Equal(axis.PositionAt(to), Cursor);
        }

        [Theory]
        [InlineData(ImGuiKey.None, ImGuiKey.Equal)]
        [InlineData(ImGuiKey.ModShift, ImGuiKey.Equal)]
        [InlineData(ImGuiKey.None, ImGuiKey.KeypadAdd)]
        public void Plus_AddsACursorInTheMiddle_AndMakesItTheActiveOne(ImGuiKey modifier, ImGuiKey plus)
        {
            imgui.Key(ImGuiKey.ModShift, ImGuiKey.RightArrow, draw);
            var first = Cursor;

            Press(modifier, plus);
            imgui.Key(ImGuiKey.RightArrow, draw);

            var positions = panel.CursorPositions();
            Assert.Equal(2, positions.Count);
            Assert.Equal(first, positions[0]);
            Assert.Equal(panel.Axis().CellColumns(Middle).End, panel.Axis().CellColumns(positions[1]).Start);
        }

        [Fact]
        public void Plus_StopsAtFourCursors()
        {
            for (int i = 0; i < 5; i++)
                imgui.Key(ImGuiKey.Equal, draw);

            Assert.Equal(4, panel.CursorPositions().Count);
        }

        [Theory]
        [InlineData(ImGuiKey.Minus)]
        [InlineData(ImGuiKey.KeypadSubtract)]
        [InlineData(ImGuiKey.Delete)]
        public void Minus_RemovesTheActiveCursor_AndHandsTheArrowsToTheReference(ImGuiKey remove)
        {
            var reference = Cursor;
            imgui.Key(ImGuiKey.Equal, draw);
            imgui.Key(ImGuiKey.ModShift, ImGuiKey.RightArrow, draw);

            imgui.Key(remove, draw);
            Assert.Equal(reference, Cursor);
            imgui.Key(ImGuiKey.RightArrow, draw);

            Assert.NotEqual(reference, Cursor);
        }

        [Fact]
        public void Minus_DoesNothing_WhileTheReferenceIsActive()
        {
            // NB: off the middle, where the panel would put a new cursor if the reference were removed.
            imgui.Key(ImGuiKey.ModShift, ImGuiKey.RightArrow, draw);
            var reference = Cursor;

            imgui.Key(ImGuiKey.Minus, draw);
            imgui.Key(ImGuiKey.Delete, draw);

            Assert.Equal(reference, Cursor);
        }

        [Fact]
        public void PlusAndMinus_DoNothing_WhileTheCursorsAreHidden()
        {
            imgui.Key(ImGuiKey.Equal, draw);
            imgui.Key(ImGuiKey.C, draw);

            imgui.Key(ImGuiKey.Equal, draw);
            imgui.Key(ImGuiKey.Minus, draw);

            Assert.Equal(2, panel.CursorPositions().Count);
        }

        [Fact]
        public void ACursorTheViewCanStillReach_KeepsItsPlace_ThroughHidingAndShowing()
        {
            imgui.Key(ImGuiKey.ModShift, ImGuiKey.RightArrow, draw);
            var placed = Cursor;

            imgui.Key(ImGuiKey.C, draw);
            imgui.Key(ImGuiKey.C, draw);

            Assert.Equal(placed, Cursor);
        }

        [Fact]
        public void ACursorLeftInARecordNoLongerShown_IsBroughtIntoViewWhenTheCursorsAreShown()
        {
            imgui.Key(ImGuiKey.Space, draw);
            for (int i = 0; i < 30; i++)
                imgui.Key(ImGuiKey.A, draw);
            panel.HomeCursors();
            var placed = Cursor;

            imgui.Key(ImGuiKey.C, draw);
            imgui.Key(ImGuiKey.Space, draw);
            imgui.Key(ImGuiKey.C, draw);

            var window = panel.Axis().Window;
            Assert.True(placed < window.Start, $"{placed} was left in view of {window.Start}..{window.End}");
            Assert.Equal(Middle, Cursor);
        }

        void Press(ImGuiKey modifier, ImGuiKey key)
        {
            if (modifier == ImGuiKey.None)
                imgui.Key(key, draw);
            else
                imgui.Key(modifier, key, draw);
        }
    }
}
