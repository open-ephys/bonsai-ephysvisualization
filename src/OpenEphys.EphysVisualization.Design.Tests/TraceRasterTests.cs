using System;
using Xunit;
using RowLayout = OpenEphys.EphysVisualization.Design.ImGuiWaveformPanel.RowLayout;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    // NB: Bin works out each pixel's columns with the same Cell the GPU's cell texture is filled from, so what it
    // gives is what the shaders draw, and what the outlines drawn over an expanded channel are placed by.
    public unsafe class TraceRasterTests
    {
        [Theory]
        [InlineData(0, 1000, 100)]
        [InlineData(0, 1000, 300)]
        [InlineData(5, 1000, 300)]
        public void CombinedBin_IsTheMinAndMaxOfThePixelsColumns(long start, int columns, int width)
        {
            var axis = new PixelAxis(new DisplayWindow(start, 1, columns), 0, width);
            var signal = TestSignal.Random(2, columns);
            var min = new float[columns];
            var max = new float[columns];
            for (int c = 0; c < columns; c++)
            {
                min[c] = Math.Min(signal[0, c], signal[1, c]);
                max[c] = Math.Max(signal[0, c], signal[1, c]);
            }

            fixed (float* minLine = min, maxLine = max)
            {
                for (int p = 0; p < width; p++)
                {
                    var (first, last) = axis.ColumnsAt(p);
                    var low = float.PositiveInfinity;
                    var high = float.NegativeInfinity;
                    for (int c = first; c < last; c++)
                    {
                        low = Math.Min(low, min[c]);
                        high = Math.Max(high, max[c]);
                    }

                    Assert.Equal((low, high), TraceRaster.Bin(minLine, maxLine, axis, p));
                }
            }
        }

        [Fact]
        public void SpreadBin_InterpolatesBetweenColumnsAtThePixelsMiddle()
        {
            const int Columns = 50;
            const int Width = 200;
            var axis = new PixelAxis(new DisplayWindow(0, 1, Columns), 0, Width);
            var min = new float[Columns];
            var max = new float[Columns];
            for (int c = 0; c < Columns; c++)
            {
                min[c] = c;
                max[c] = c + 10;
            }

            fixed (float* minLine = min, maxLine = max)
            {
                for (int p = 0; p < Width; p++)
                {
                    var x = Math.Min(Columns - 1, (p + 0.5f) * Columns / Width);
                    var (low, high) = TraceRaster.Bin(minLine, maxLine, axis, p);
                    Assert.Equal(x, low, 4);
                    Assert.Equal(x + 10, high, 4);
                }
            }
        }

        // NB: the arithmetic below is the shaders' own, from TraceRaster.Heatmap.frag and TraceRaster.Traces.vert,
        // with the texture placed at the plot's top rounded down as TraceRaster places it.
        [Theory]
        [InlineData(100)]
        [InlineData(100.4f)]
        [InlineData(100.75f)]
        public void ShaderRows_LandOnThePixelRowsDrawnOverThem_WhereverThePlotsTopFalls(float top)
        {
            using var envelope = new Envelope(384, 100);
            var layout = new RowLayout(0, 384, 2.6f, top, top + 500, top - 37.3f);
            var frame = new PlotFrame(
                envelope, layout, new PixelAxis(new DisplayWindow(0, 1, 100), 0, 100), top, top + 500, 1000,
                false, 0, 1, 1, 200, 25, "µV", new bool[384], -1, -1, true, 0);
            var corner = MathF.Floor(top);
            var origin = Private.Call<float>(typeof(TraceRaster), "Origin", frame);
            var (_, scale) = frame.ValueMapping(layout.FirstVisible);

            for (int channel = layout.FirstVisible; channel < layout.LastVisible; channel++)
            {
                var edge = corner + MathF.Floor(origin + (channel - layout.FirstRow) * layout.RowHeight);
                Assert.Equal(MathF.Floor(layout.RowTop(channel)), edge);

                var offset = corner + origin + (channel - layout.FirstRow + 0.5f) * layout.RowHeight + (float)frame.Center * scale;
                Assert.InRange(offset - frame.ValueMapping(channel).Offset, -1e-3f, 1e-3f);
            }
        }

        // NB: the outline over an expanded channel joins each column's sample at the column's left edge, so the trace
        // drawn beneath it sits under it where each pixel shows that line's value at the pixel's middle, and past the
        // last column, the last sample.
        [Theory]
        [InlineData(50, 200)]
        [InlineData(37, 100)]
        public void SpreadBin_IsTheOutlinesValueAtEachPixelsMiddle(int columns, int width)
        {
            var axis = new PixelAxis(new DisplayWindow(0, 1, columns), 0, width);
            var signal = TestSignal.Random(1, columns);
            var line = new float[columns];
            for (int c = 0; c < columns; c++)
                line[c] = signal[0, c];

            fixed (float* minLine = line)
            {
                for (int p = 0; p < width; p++)
                {
                    var x = p + 0.5f;
                    var c = 0;
                    while (c < columns - 1 && axis.ColumnX(c + 1) <= x)
                        c++;

                    var expected = c == columns - 1 ? line[c]
                        : line[c] + (x - axis.ColumnX(c)) / (axis.ColumnX(c + 1) - axis.ColumnX(c)) * (line[c + 1] - line[c]);
                    var (low, _) = TraceRaster.Bin(minLine, minLine, axis, p);
                    Assert.InRange(low - expected, -0.01f, 0.01f);
                }
            }
        }
    }
}
