using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class TimebaseTests
    {
        const int SampleRate = 1000;

        [Fact]
        public void StandardTimeBases_RunInOneTwoAndAHalfFiveSteps_UpToWhatTheHistoryHolds()
        {
            using var panel = Panel(historySeconds: 12);
            Write(panel, samples: 100);

            Assert.Equal(new[] { 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10 }, panel.StandardTimeBases,
                new Tolerance());
        }

        [Fact]
        public void StandardTimeBases_AlwaysOfferTheShortest()
        {
            using var panel = Panel(historySeconds: 0.005);
            Write(panel, samples: 5);

            Assert.Equal(new[] { 0.01 }, panel.StandardTimeBases, new Tolerance());
        }

        [Fact]
        public void WhilePaused_OnlyTheTimeBasesTheRecordCanFillAreOffered()
        {
            using var panel = Panel(historySeconds: 12);
            Write(panel, samples: 3200);

            panel.Paused = true;
            Assert.Equal(2.5, panel.StandardTimeBases.Last(), 12);

            panel.Paused = false;
            Assert.Equal(10, panel.StandardTimeBases.Last(), 12);
        }

        [Fact]
        public void WhilePaused_ARecordShorterThanTheShortestTimebaseStillOffersIt()
        {
            using var panel = Panel(historySeconds: 12);
            Write(panel, samples: 5);

            panel.Paused = true;

            Assert.Equal(new[] { 0.01 }, panel.StandardTimeBases, new Tolerance());
        }

        [Fact]
        public void Timebase_IsNoLongerThanThePausedRecordCanFill()
        {
            var settings = new LfpViewer { Timebase = 10 };
            using var panel = Panel(historySeconds: 12, settings);
            Write(panel, samples: 3200);
            Assert.Equal(10, panel.Timebase, 12);

            panel.Paused = true;

            Assert.Equal(2.5, panel.Timebase, 12);
            Assert.Equal(10, settings.Timebase, 12);
        }

        static ImGuiWaveformPanel Panel(double historySeconds, LfpViewer? settings = null) =>
            new(settings ?? new LfpViewer(), (long)(historySeconds * SampleRate * sizeof(float)), maxColumns: 1000);

        static void Write(ImGuiWaveformPanel panel, int samples)
        {
            TestSignal.Feed(TestSignal.Random(1, samples), new[] { 100 }, block => panel.Update(block, SampleRate));
        }

        // NB: the ladder is built by repeated multiplication, which drifts from the decimal values in the last bits.
        class Tolerance : IEqualityComparer<double>
        {
            public bool Equals(double x, double y) => Math.Abs(x - y) <= 1e-12 * Math.Max(1, Math.Abs(x));

            public int GetHashCode(double value) => 0;
        }
    }
}
