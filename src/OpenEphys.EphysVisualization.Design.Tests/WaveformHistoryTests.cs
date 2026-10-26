using System;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class WaveformHistoryTests
    {
        const int Rows = 3;
        const int Capacity = 100;

        static readonly int[] BlockSizes = { 37, 11, 64, 3 };

        [Fact]
        public void Count_AndOldest_TrackTheTimelineAsTheRingFills()
        {
            using var history = new WaveformHistory(Rows, Capacity);
            Assert.Equal(0, history.Count);
            Assert.Equal(0, history.Oldest);

            TestSignal.Feed(TestSignal.Random(Rows, 60), BlockSizes, history.Write);
            Assert.Equal(60, history.Count);
            Assert.Equal(0, history.Oldest);

            TestSignal.Feed(TestSignal.Random(Rows, 190), BlockSizes, history.Write);
            Assert.Equal(250, history.Count);
            Assert.Equal(250 - Capacity, history.Oldest);
        }

        [Theory]
        [InlineData(150, 100)]
        [InlineData(180, 40)]
        [InlineData(195, 10)]
        [InlineData(249, 1)]
        public void Decimate_ReadsBackTheSamplesWritten_AcrossTheRingsEnd(int first, int samples)
        {
            var signal = TestSignal.Random(Rows, 250);
            using var history = new WaveformHistory(Rows, Capacity);
            TestSignal.Feed(signal, BlockSizes, history.Write);

            using var read = new Decimator(Rows, samples, 1);
            Assert.True(history.Decimate(first, samples, read));

            AssertSamples(signal, first, samples, read);
        }

        [Fact]
        public void Write_OfABlockLongerThanTheRing_KeepsItsTailAndAdvancesByTheWholeBlock()
        {
            var signal = TestSignal.Random(Rows, 30 + 250);
            using var history = new WaveformHistory(Rows, Capacity);
            using (var head = TestSignal.Block(signal, 0, 30))
                history.Write(head);
            using (var block = TestSignal.Block(signal, 30, 250))
                history.Write(block);

            Assert.Equal(280, history.Count);
            Assert.Equal(280 - Capacity, history.Oldest);

            using var read = new Decimator(Rows, Capacity, 1);
            Assert.True(history.Decimate(history.Oldest, Capacity, read));
            AssertSamples(signal, (int)history.Oldest, Capacity, read);
        }

        [Theory]
        [InlineData(149, 10)]
        [InlineData(245, 10)]
        [InlineData(200, -1)]
        public void Decimate_OfARangeNotHeld_FailsAndReducesNothing(int first, int samples)
        {
            using var history = new WaveformHistory(Rows, Capacity);
            TestSignal.Feed(TestSignal.Random(Rows, 250), BlockSizes, history.Write);

            using var read = new Decimator(Rows, 20, 1);
            Assert.False(history.Decimate(first, samples, read));

            Assert.Equal(0, read.Cursor);
            Assert.True(double.IsNaN(read.Sweep.Min.GetReal(0, 0)));
        }

        [Fact]
        public void Clear_AbandonsWhatIsHeld_WithoutMovingTheTimeline()
        {
            var signal = TestSignal.Random(Rows, 120);
            using var history = new WaveformHistory(Rows, Capacity);
            using (var before = TestSignal.Block(signal, 0, 80))
                history.Write(before);

            history.Clear();
            Assert.Equal(80, history.Count);
            Assert.Equal(80, history.Oldest);

            using (var after = TestSignal.Block(signal, 80, 40))
                history.Write(after);

            using var read = new Decimator(Rows, 40, 1);
            Assert.False(history.Decimate(79, 2, read));
            Assert.True(history.Decimate(80, 40, read));
            AssertSamples(signal, 80, 40, read);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 0)]
        public void Constructor_RejectsNoChannelsOrNoCapacity(int rows, int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WaveformHistory(rows, capacity));
        }

        // NB: a decimator at one sample per column holds each sample as both its minimum and maximum.
        static void AssertSamples(float[,] signal, int first, int samples, Decimator read)
        {
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < samples; column++)
                {
                    Assert.Equal(signal[row, first + column], read.Sweep.Min.GetReal(row, column));
                    Assert.Equal(signal[row, first + column], read.Sweep.Max.GetReal(row, column));
                }
            }
        }
    }
}
