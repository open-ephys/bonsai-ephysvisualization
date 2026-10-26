using System.Globalization;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class PlotTextTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(0.0123456, 0.0123)]
        [InlineData(1.23456, 1.23)]
        [InlineData(12.3456, 12.3)]
        [InlineData(123.456, 123)]
        [InlineData(-45.678, -45.7)]
        [InlineData(0.99987, 1)]
        [InlineData(1234.56, 1230)]
        [InlineData(12345, 12300)]
        [InlineData(-98765, -98800)]
        [InlineData(4.5678e7, 4.57e7)]
        public void Significant_RoundsToThreeSignificantFigures(double value, double rounded)
        {
            Assert.Equal(rounded, PlotText.Significant(value), 12);
        }

        [Theory]
        [InlineData(0.0123456, "0.0123")]
        [InlineData(0.000456789, "0.000457")]
        [InlineData(1.23456, "1.23")]
        [InlineData(0.3 * 3, "0.9")]
        [InlineData(12345, "12300")]
        [InlineData(4.5678e7, "45700000")]
        public void Significant_PrintsAsWritten(double value, string printed)
        {
            Assert.Equal(printed, PlotText.Significant(value).ToString(CultureInfo.InvariantCulture));
        }
    }
}
