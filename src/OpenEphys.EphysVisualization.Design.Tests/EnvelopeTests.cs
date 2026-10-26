using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class EnvelopeTests
    {
        [Fact]
        public void New_IsEmpty()
        {
            using var envelope = new Envelope(2, 5);

            AssertEmpty(envelope);
        }

        [Fact]
        public void Clear_EmptiesEveryColumn()
        {
            using var envelope = Filled();

            envelope.Clear();

            AssertEmpty(envelope);
        }

        [Fact]
        public void CopyTo_CopiesTheMinimaAndMaxima()
        {
            using var source = Filled();
            using var destination = new Envelope(2, 5);

            source.CopyTo(destination);

            AssertEqual(source, destination);
        }

        [Fact]
        public void Clone_IsUnaffectedByLaterChangesToTheOriginal()
        {
            using var original = Filled();
            using var clone = original.Clone();

            original.Clear();

            Assert.Equal(-3.0, clone.Min.GetReal(1, 4));
            Assert.Equal(4.0, clone.Max.GetReal(1, 4));
        }

        static Envelope Filled()
        {
            var envelope = new Envelope(2, 5);
            for (int row = 0; row < envelope.Rows; row++)
            {
                for (int column = 0; column < envelope.Cols; column++)
                {
                    envelope.Min.SetReal(row, column, -row - column + 2);
                    envelope.Max.SetReal(row, column, row + column - 1);
                }
            }

            return envelope;
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

        static void AssertEqual(Envelope expected, Envelope actual)
        {
            for (int row = 0; row < expected.Rows; row++)
            {
                for (int column = 0; column < expected.Cols; column++)
                {
                    Assert.Equal(expected.Min.GetReal(row, column), actual.Min.GetReal(row, column));
                    Assert.Equal(expected.Max.GetReal(row, column), actual.Max.GetReal(row, column));
                }
            }
        }
    }
}
