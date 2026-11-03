using System.Collections.Generic;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class EventViewTests
    {
        const float Span = 1000;
        static readonly float Knee = ImGuiEventView.Knee(Span);

        [Fact]
        public void AnAmplitude_ComesBackFromItsBin()
        {
            foreach (var value in new[] { -900f, -120f, -3f, 0f, 0.5f, 40f, 750f })
            {
                var bin = ImGuiEventView.BinOf(value, Span, Knee);
                Assert.Equal(value, ImGuiEventView.ValueOf(bin, Span, Knee), value == 0 ? 1e-3 : System.Math.Abs(value) * 1e-4);
            }
        }

        [Fact]
        public void Bins_AreFineNearZero_AndCoarserFarFromIt_KeptToTheTile()
        {
            float Width(float value) =>
                ImGuiEventView.ValueOf(ImGuiEventView.BinOf(value, Span, Knee) + 1, Span, Knee) - value;

            Assert.True(Width(0) < 1, $"near zero {Width(0)}");
            Assert.True(Width(500) > 10 * Width(0), $"at 500 {Width(500)}");
            Assert.Equal(1024 / 2f, ImGuiEventView.BinOf(0, Span, Knee), 3);
            Assert.Equal(1024 - 0.5f, ImGuiEventView.BinOf(5 * Span, Span, Knee));
            Assert.Equal(0.5f, ImGuiEventView.BinOf(-5 * Span, Span, Knee));
        }

        [Fact]
        public void EachChannel_HasATileOfItsOwn_InRowsOfTheAtlas()
        {
            var width = ImGuiEventView.TileWidth(60);

            Assert.Equal((0f, 0f), ImGuiEventView.TileOrigin(0, 10, width));
            Assert.Equal((3f * width, 0f), ImGuiEventView.TileOrigin(3, 10, width));
            Assert.Equal((2f * width, 1024f), ImGuiEventView.TileOrigin(12, 10, width));
        }

        [Fact]
        public void AWaveform_IsDrawnAsLinesBetweenItsSamples_AcrossItsTile()
        {
            var points = new List<float>();
            var width = ImGuiEventView.TileWidth(4);

            ImGuiEventView.AppendSegments(new[] { 1f, 2f, 3f, 4f }, 4, 100, 512, points);

            Assert.Equal(3 * 2 * 3, points.Count);
            Assert.Equal(new[] { 100.5f, 512f, 1f, 100.5f + (width - 1) / 3f, 512f, 2f }, points.GetRange(0, 6));
            Assert.Equal(100.5f + width - 1, points[points.Count - 3]);
            Assert.Equal(4f, points[points.Count - 1]);
        }

        [Fact]
        public void Waveforms_AddedWhileNoneAreDrawn_AreHeldOnlyUpToTheLimit_AndClearReleasesThem()
        {
            const int Length = 45;
            var view = new ImGuiEventView(4, Length, 15, 30000, Span);
            var waveform = new float[Length];

            for (int i = 0; i < 2 * ImGuiEventView.MostPendingSamples / Length; i++)
                view.Add(i % 4, waveform);

            Assert.True(view.PendingSamples <= ImGuiEventView.MostPendingSamples, $"{view.PendingSamples} held");
            Assert.True(view.PendingSamples > ImGuiEventView.MostPendingSamples - Length, $"{view.PendingSamples} held");

            view.Clear();
            Assert.Equal(0, view.PendingSamples);
            view.Add(0, waveform);
            Assert.Equal(Length, view.PendingSamples);
        }

        [Fact]
        public void Waveforms_AddedWhilePaused_AreDropped()
        {
            const int Length = 45;
            var view = new ImGuiEventView(4, Length, 15, 30000, Span) { Paused = true };

            view.Add(0, new float[Length]);

            Assert.Equal(0, view.PendingSamples);
        }

        [Fact]
        public void ASteadyEventRate_SettlesAtItself()
        {
            const double Rate = 20;
            const double FrameSeconds = 0.01;
            var rates = new double[2];
            var events = new int[2];

            // NB: one event every fifth frame, on channel 0 only, for ten time constants.
            for (int frame = 0; frame < 1000; frame++)
            {
                if (frame % 5 == 0)
                    events[0] = 1;
                ImGuiEventView.AdvanceRates(rates, events, FrameSeconds, 1);
            }

            Assert.Equal(Rate, rates[0], 0.1 * Rate);
            Assert.Equal(0, rates[1]);
            Assert.Equal(new[] { 0, 0 }, events);
        }

        [Fact]
        public void ARate_HoldsWhileNoTimePasses_AndDecaysWithTheTimeConstant()
        {
            var rates = new double[] { 10 };
            var events = new int[1];

            ImGuiEventView.AdvanceRates(rates, events, 0, 1);
            Assert.Equal(10, rates[0], 12);

            ImGuiEventView.AdvanceRates(rates, events, 1, 1);
            Assert.Equal(10 / System.Math.E, rates[0], 12);
        }
    }
}
