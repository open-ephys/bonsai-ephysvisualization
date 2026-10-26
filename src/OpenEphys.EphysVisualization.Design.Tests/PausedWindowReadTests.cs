using System;
using OpenCV.Net;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    // A paused viewer can show its frozen frame either as the live decimator's columns copied at the pause, or by
    // reading the frame back out of the history. These check how far the two agree, column by column: whether the
    // history read lands on the live decimator's column boundaries, and where it does not.
    public class PausedWindowReadTests
    {
        const int Rows = 3;
        const int Columns = 40;
        const int Factor = 7;

        // NB: block sizes that do not divide the column width, so column boundaries fall inside blocks.
        static readonly int[] BlockSizes = { 13, 29, 101, 5, 64 };

        [Theory]
        [InlineData(2 * Columns * Factor + 3)]
        [InlineData(2 * Columns * Factor + 7 * Factor)]
        [InlineData(3 * Columns * Factor + 17 * Factor + 4)]
        [InlineData(5 * Columns * Factor - 1)]
        [InlineData(4 * Columns * Factor)]
        public void HistoryRead_MatchesLiveColumns_ExceptTheCursorColumn(int samples)
        {
            using var live = new Decimator(Rows, Columns, Factor);
            using var history = new WaveformHistory(Rows, 10 * Columns * Factor);
            Feed(samples, live, history);

            var newest = history.Count;
            var origin = ImGuiWaveformPanel.SweepOrigin(newest, live);
            var frozen = new DisplayWindow(0, Factor, Columns);
            using var read = new Decimator(Rows, Columns, Factor);
            Assert.True(ImGuiWaveformPanel.ReadWindow(history, read, frozen, origin, newest - origin, frozen.Span));

            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    var liveMin = live.Sweep.Min.GetReal(row, column);
                    var liveMax = live.Sweep.Max.GetReal(row, column);
                    var readMin = read.Sweep.Min.GetReal(row, column);
                    var readMax = read.Sweep.Max.GetReal(row, column);

                    if (column != live.Cursor || live.Filled == 0)
                    {
                        Assert.Equal(liveMin, readMin);
                        Assert.Equal(liveMax, readMax);
                        continue;
                    }

                    // NB: the live cursor column holds only the samples of the new sweep reduced into it so far,
                    // while the read also takes in the rest of that column from the previous sweep's tail.
                    Assert.True(readMin <= liveMin);
                    Assert.True(readMax >= liveMax);
                }
            }
        }

        [Fact]
        public void HistoryRead_DiffersFromLiveColumns_AtAPartFilledCursorColumn()
        {
            using var live = new Decimator(Rows, Columns, Factor);
            using var history = new WaveformHistory(Rows, 10 * Columns * Factor);
            Feed(2 * Columns * Factor + 10 * Factor + 3, live, history);
            Assert.NotEqual(0, live.Filled);

            var newest = history.Count;
            var origin = ImGuiWaveformPanel.SweepOrigin(newest, live);
            var frozen = new DisplayWindow(0, Factor, Columns);
            using var read = new Decimator(Rows, Columns, Factor);
            Assert.True(ImGuiWaveformPanel.ReadWindow(history, read, frozen, origin, newest - origin, frozen.Span));

            var differs = false;
            for (int row = 0; row < Rows; row++)
            {
                differs |= live.Sweep.Min.GetReal(row, live.Cursor) != read.Sweep.Min.GetReal(row, live.Cursor) ||
                    live.Sweep.Max.GetReal(row, live.Cursor) != read.Sweep.Max.GetReal(row, live.Cursor);
            }

            Assert.True(differs);
        }

        static void Feed(int samples, Decimator live, WaveformHistory history)
        {
            var random = new Random(1234);
            var written = 0;
            for (int b = 0; written < samples; b++)
            {
                var size = Math.Min(BlockSizes[b % BlockSizes.Length], samples - written);
                using var block = new Mat(Rows, size, Depth.F32, 1);
                for (int row = 0; row < Rows; row++)
                {
                    for (int column = 0; column < size; column++)
                        block.SetReal(row, column, random.NextDouble() * 200 - 100);
                }

                live.Process(block);
                history.Write(block);
                written += size;
            }
        }
    }
}
