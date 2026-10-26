using System;
using OpenCV.Net;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    static class TestSignal
    {
        // NB: whole numbers, which a 32-bit float holds exactly, so that a reduction can be compared with its
        // reference for equality.
        public static float[,] Random(int rows, int samples, int seed = 1234)
        {
            var random = new Random(seed);
            var signal = new float[rows, samples];
            for (int row = 0; row < rows; row++)
            {
                for (int sample = 0; sample < samples; sample++)
                    signal[row, sample] = random.Next(-1000, 1000);
            }

            return signal;
        }

        public static Mat Block(float[,] signal, int first, int count)
        {
            var block = new Mat(signal.GetLength(0), count, Depth.F32, 1);
            for (int row = 0; row < block.Rows; row++)
            {
                for (int column = 0; column < count; column++)
                    block.SetReal(row, column, signal[row, first + column]);
            }

            return block;
        }

        /// <summary>
        /// Hands <paramref name="signal"/> to <paramref name="sink"/> in blocks, cycling through
        /// <paramref name="blockSizes"/>.
        /// </summary>
        public static void Feed(float[,] signal, int[] blockSizes, Action<Mat> sink)
        {
            var samples = signal.GetLength(1);
            for (int b = 0, written = 0; written < samples; b++)
            {
                var size = Math.Min(blockSizes[b % blockSizes.Length], samples - written);
                using var block = Block(signal, written, size);
                sink(block);
                written += size;
            }
        }
    }
}
