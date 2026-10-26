using System;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class DecimatorTests
    {
        const int Rows = 3;
        const int Columns = 20;
        const int Factor = 7;

        // NB: block sizes that do not divide the column width, so column boundaries fall inside blocks.
        static readonly int[] BlockSizes = { 13, 29, 101, 5, 64 };

        [Theory]
        [InlineData(1)]
        [InlineData(Factor)]
        [InlineData(10 * Factor + 3)]
        [InlineData(Columns * Factor)]
        [InlineData(3 * Columns * Factor + 11 * Factor + 4)]
        public void Sweep_HoldsTheMinAndMaxOfEachColumnsLatestSamples(int samples)
        {
            var signal = TestSignal.Random(Rows, samples);
            using var decimator = new Decimator(Rows, Columns, Factor);
            TestSignal.Feed(signal, BlockSizes, decimator.Process);

            AssertSweep(signal, samples, decimator.Sweep);
            Assert.Equal(samples / Factor % Columns, decimator.Cursor);
            Assert.Equal(samples % Factor, decimator.Filled);
        }

        [Fact]
        public void Process_OfOneWholeSweepAfterReset_FillsEveryColumnAndReturnsTheCursorToZero()
        {
            var signal = TestSignal.Random(Rows, Columns * Factor);
            using var decimator = new Decimator(Rows, Columns, Factor);
            using (var partial = TestSignal.Block(TestSignal.Random(Rows, 3 * Factor + 2, seed: 99), 0, 3 * Factor + 2))
                decimator.Process(partial);

            decimator.Reset();
            using var block = TestSignal.Block(signal, 0, Columns * Factor);
            decimator.Process(block);

            Assert.Equal(0, decimator.Cursor);
            Assert.Equal(0, decimator.Filled);
            AssertSweep(signal, Columns * Factor, decimator.Sweep);
        }

        [Fact]
        public void Reset_EmptiesEveryColumn()
        {
            using var decimator = new Decimator(Rows, Columns, Factor);
            TestSignal.Feed(TestSignal.Random(Rows, 5 * Factor + 3), BlockSizes, decimator.Process);

            decimator.Reset();

            Assert.Equal(0, decimator.Cursor);
            Assert.Equal(0, decimator.Filled);
            AssertEmpty(decimator.Sweep);
        }

        [Fact]
        public void LastSweep_IsNullUntilTheCursorFirstWraps()
        {
            using var decimator = new Decimator(Rows, Columns, Factor);
            TestSignal.Feed(TestSignal.Random(Rows, Columns * Factor - 1), BlockSizes, decimator.Process);

            Assert.Null(decimator.LastSweep);
        }

        [Fact]
        public void LastSweep_HoldsTheSweepAsItWasAtTheLatestWrap()
        {
            var samples = 2 * Columns * Factor + 9 * Factor + 5;
            var signal = TestSignal.Random(Rows, samples);
            using var decimator = new Decimator(Rows, Columns, Factor);
            TestSignal.Feed(signal, BlockSizes, decimator.Process);

            Assert.NotNull(decimator.LastSweep);
            AssertSweep(signal, samples / (Columns * Factor) * Columns * Factor, decimator.LastSweep!);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 0)]
        public void Constructor_RejectsAnEmptyColumnCountOrFactor(int length, int factor)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Decimator(Rows, length, factor));
        }

        // NB: column c last held bin b, the latest with b % Columns == c that the samples reached, which spans
        // samples b * Factor up to the next bin or the last sample, whichever comes first.
        static void AssertSweep(float[,] signal, int samples, Envelope sweep)
        {
            var bins = (samples + Factor - 1) / Factor;
            for (int column = 0; column < Columns; column++)
            {
                var bin = bins - 1 - ((bins - 1 - column) % Columns + Columns) % Columns;
                for (int row = 0; row < Rows; row++)
                {
                    var actualMin = sweep.Min.GetReal(row, column);
                    var actualMax = sweep.Max.GetReal(row, column);
                    if (bin < 0)
                    {
                        Assert.True(double.IsNaN(actualMin));
                        Assert.True(double.IsNaN(actualMax));
                        continue;
                    }

                    var min = float.PositiveInfinity;
                    var max = float.NegativeInfinity;
                    for (int sample = bin * Factor; sample < Math.Min((bin + 1) * Factor, samples); sample++)
                    {
                        min = Math.Min(min, signal[row, sample]);
                        max = Math.Max(max, signal[row, sample]);
                    }

                    Assert.Equal(min, actualMin);
                    Assert.Equal(max, actualMax);
                }
            }
        }

        static void AssertEmpty(Envelope envelope)
        {
            for (int row = 0; row < envelope.Rows; row++)
            {
                for (int column = 0; column < envelope.Cols; column++)
                {
                    Assert.True(double.IsNaN(envelope.Min.GetReal(row, column)));
                    Assert.True(double.IsNaN(envelope.Max.GetReal(row, column)));
                }
            }
        }
    }
}
