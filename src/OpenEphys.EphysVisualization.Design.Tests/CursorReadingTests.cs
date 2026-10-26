using Xunit;
using RowLayout = OpenEphys.EphysVisualization.Design.ImGuiWaveformPanel.RowLayout;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class CursorReadingTests
    {
        const int Rows = 3;
        const int Columns = 100;

        [Fact]
        public void TryRead_GivesTheMinAndMaxOfTheCellOnTheSelectedChannel()
        {
            using var envelope = Ramp();
            var cursors = new WaveformCursors { Show = true };

            // NB: two columns to a pixel, so position 41 falls in the cell of columns 40 and 41.
            Assert.True(TryRead(cursors, Frame(envelope, width: 50, selected: 1), 41, out var min, out var max));

            Assert.Equal(Min(1, 40), min);
            Assert.Equal(Max(1, 41), max);
        }

        [Fact]
        public void TryRead_OfACellOfOneColumn_GivesThatColumn()
        {
            using var envelope = Ramp();
            var cursors = new WaveformCursors { Show = true };

            Assert.True(TryRead(cursors, Frame(envelope, width: 200, selected: 2), 41, out var min, out var max));

            Assert.Equal(Min(2, 41), min);
            Assert.Equal(Max(2, 41), max);
        }

        [Fact]
        public void TryRead_InTheHeatmap_GivesTheOneValueFurtherFromZero()
        {
            using var envelope = new Envelope(Rows, Columns);
            envelope.Min.SetReal(0, 10, -30);
            envelope.Max.SetReal(0, 10, 20);
            envelope.Min.SetReal(0, 11, -5);
            envelope.Max.SetReal(0, 11, 20);
            var cursors = new WaveformCursors { Show = true };

            Assert.True(TryRead(cursors, Frame(envelope, width: 200, selected: 0, heatmap: true), 10, out var min, out var max));
            Assert.Equal((-30.0, -30.0), (min, max));
            Assert.True(TryRead(cursors, Frame(envelope, width: 200, selected: 0, heatmap: true), 11, out min, out max));
            Assert.Equal((20.0, 20.0), (min, max));
        }

        [Fact]
        public void TryRead_ReadsTheExpandedChannel_OverTheSelectedOne_EvenWhenHidden()
        {
            using var envelope = Ramp();
            var cursors = new WaveformCursors { Show = true };
            var hidden = new bool[Rows];
            hidden[2] = true;

            Assert.True(TryRead(cursors, Frame(envelope, width: 200, selected: 0, expanded: 2, hidden: hidden), 41,
                out var min, out _));

            Assert.Equal(Min(2, 41), min);
        }

        [Fact]
        public void TryRead_FailsWithNothingToRead()
        {
            using var envelope = Ramp();
            var hidden = new bool[Rows];
            hidden[1] = true;
            var shown = new WaveformCursors { Show = true };

            Assert.False(TryRead(new WaveformCursors(), Frame(envelope, width: 200, selected: 1), 41, out _, out _));
            Assert.False(TryRead(shown, Frame(envelope, width: 200, selected: -1), 41, out _, out _));
            Assert.False(TryRead(shown, Frame(envelope, width: 200, selected: 1, hidden: hidden), 41, out _, out _));
            Assert.False(TryRead(shown, Frame(envelope, width: 200, selected: 1), Columns + 5, out _, out _));

            envelope.Clear();
            Assert.False(TryRead(shown, Frame(envelope, width: 200, selected: 1), 41, out _, out _));
        }

        static bool TryRead(WaveformCursors cursors, in PlotFrame frame, long position, out double min, out double max)
        {
            var args = new object?[] { frame, position, null, null };
            var read = Private.Call<bool>(cursors, nameof(TryRead), args);
            (min, max) = ((double)args[2]!, (double)args[3]!);
            return read;
        }

        static double Min(int row, int column) => 100 * row + column;

        static double Max(int row, int column) => 100 * row + column + 0.5;

        static Envelope Ramp()
        {
            var envelope = new Envelope(Rows, Columns);
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    envelope.Min.SetReal(row, column, Min(row, column));
                    envelope.Max.SetReal(row, column, Max(row, column));
                }
            }

            return envelope;
        }

        static PlotFrame Frame(
            Envelope envelope, int width, int selected, int expanded = -1, bool heatmap = false, bool[]? hidden = null) => new(
            envelope, new RowLayout(0, Rows, 40, 100, 600, 100), new PixelAxis(new DisplayWindow(0, 1, Columns), 0, width),
            100, 600, 1000, true, 0, 1, 1, 200, 0, "µV", hidden ?? new bool[Rows], expanded, selected, heatmap, 0);
    }
}
