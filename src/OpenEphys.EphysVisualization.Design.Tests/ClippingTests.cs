using OpenCV.Net;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class ClippingTests
    {
        [Theory]
        [InlineData(Depth.U8, byte.MinValue, byte.MaxValue)]
        [InlineData(Depth.S8, sbyte.MinValue, sbyte.MaxValue)]
        [InlineData(Depth.U16, ushort.MinValue, ushort.MaxValue)]
        [InlineData(Depth.S16, short.MinValue, short.MaxValue)]
        [InlineData(Depth.S32, int.MinValue, int.MaxValue)]
        public void AnIntegerDepth_ClipsAtTheEndsOfItsType(Depth depth, double low, double high)
        {
            var (least, greatest) = DepthLimits(depth);
            Assert.Equal(low, least);
            Assert.Equal(high, greatest);

            // NB: mid-scale, since zero is the bottom of an unsigned depth's range and would clip every row.
            using var raw = new Mat(3, 4, depth, 1);
            raw.Set(Scalar.All(System.Math.Floor((low + high) / 2)));
            raw.SetReal(0, 1, low);
            raw.SetReal(2, 3, high);

            Assert.Equal(new[] { true, false, true }, ClippedRows(raw, least, greatest));
        }

        [Theory]
        [InlineData(Depth.F32)]
        [InlineData(Depth.F64)]
        public void AFloatingDepth_IsNotChecked_UnlessALimitIsSet(Depth depth)
        {
            var (least, greatest) = DepthLimits(depth);
            Assert.Null(least);
            Assert.Null(greatest);

            using var raw = new Mat(2, 4, depth, 1);
            raw.SetZero();
            raw.SetReal(1, 2, 1e9);

            Assert.Null(ClippedRows(raw, least, greatest));
            Assert.Equal(new[] { false, true }, ClippedRows(raw, null, 1000));
        }

        [Fact]
        public void ASampleAtTheLimit_Clips_AndOneInsideItDoesNot()
        {
            using var raw = new Mat(3, 2, Depth.S16, 1);
            raw.SetZero();
            raw.SetReal(0, 0, -500);
            raw.SetReal(1, 0, -499);
            raw.SetReal(2, 1, 500);

            Assert.Equal(new[] { true, false, true }, ClippedRows(raw, -500, 500));
        }

        [Fact]
        public void ACleanBlock_SendsNothing()
        {
            using var raw = new Mat(3, 4, Depth.S16, 1);
            raw.SetZero();

            Assert.Null(ClippedRows(raw, short.MinValue, short.MaxValue));
        }

        static (double? Low, double? High) DepthLimits(Depth depth) =>
            Private.Call<(double?, double?)>(typeof(WaveformScopeVisualizer), nameof(DepthLimits), depth);

        static bool[]? ClippedRows(Mat raw, double? low, double? high) =>
            Private.Call<bool[]?>(typeof(WaveformScopeVisualizer), nameof(ClippedRows), raw, low, high);
    }
}
