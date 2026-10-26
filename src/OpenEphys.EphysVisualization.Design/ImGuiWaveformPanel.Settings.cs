using System;
using System.Collections.Generic;
using System.Numerics;
using Hexa.NET.ImGui;

namespace OpenEphys.EphysVisualization.Design
{
    partial class ImGuiWaveformPanel
    {
        /// <summary>
        /// Smallest amplitude a channel's row may span.
        /// </summary>
        /// <remarks>
        /// Ranges are in whatever unit the data arrives in, so this is far below any range in use, only keeping the
        /// amplitude zoom and the labels away from zero.
        /// </remarks>
        public const double MinRange = 1e-6;

        static readonly double[] RangeSteps = { 1.0, 2.5, 5.0 };

        /// <summary>
        /// Presets offered below and above the range nearest the current one.
        /// </summary>
        const int RangesBelow = 3;
        const int RangesAbove = 4;

        /// <summary>
        /// The <paramref name="index"/>th range of the 1, 2.5, 5 sequence, counted from 1 at zero.
        /// </summary>
        // NB: a negative decade divides rather than multiplies, since 10^-n is inexact and 2.5 / 100 rounds to 0.025
        // where 2.5 * 0.01 does not.
        static double RangeAt(int index)
        {
            var decade = (int)Math.Floor(index / (double)RangeSteps.Length);
            var step = RangeSteps[index - decade * RangeSteps.Length];
            return decade >= 0 ? step * Math.Pow(10, decade) : step / Math.Pow(10, -decade);
        }

        /// <summary>
        /// Index of the largest range in the sequence at or below <paramref name="value"/>, and whether
        /// <paramref name="value"/> is that range.
        /// </summary>
        static (int Index, bool Exact) RangeIndex(double value)
        {
            // NB: down from the decade above the logarithm's, since Log10 of an exact power of ten can land just below
            // it.
            var index = ((int)Math.Floor(Math.Log10(value)) + 2) * RangeSteps.Length - 1;
            while (RangeAt(index) > value * (1 + 1e-9))
                index--;

            return (index, Math.Abs(RangeAt(index) - value) <= value * 1e-9);
        }

        /// <summary>
        /// Smallest height a channel row may be given in pixels.
        /// </summary>
        public const int MinChannelHeight = 1;

        /// <summary>
        /// Pixels the channel height steps by from the control strip.
        /// </summary>
        public const int ChannelHeightStep = 5;

        /// <summary>
        /// Amplitude ranges offered as presets, those of the sequence around the current range.
        /// </summary>
        public IReadOnlyList<double> StandardRanges
        {
            get
            {
                var (index, _) = RangeIndex(Range);
                if (index != standardRangesIndex)
                {
                    var first = Math.Max(RangeIndex(MinRange).Index, index - RangesBelow);
                    standardRanges = new double[index + RangesAbove - first + 1];
                    for (int i = 0; i < standardRanges.Length; i++)
                        standardRanges[i] = RangeAt(first + i);

                    standardRangesIndex = index;
                }

                return standardRanges;
            }
        }

        double[] standardRanges = Array.Empty<double>();
        int standardRangesIndex = int.MinValue;

        /// <summary>
        /// Timebases offered as presets. While paused, only those the history can fill.
        /// </summary>
        public IReadOnlyList<double> StandardTimeBases =>
            new ArraySegment<double>(standardTimeBases, 0, ServableTimeBases);

        /// <summary>
        /// How many of the offered timebases fit in what the history holds while paused.
        /// </summary>
        int ServableTimeBases
        {
            get
            {
                if (!Paused || history is null)
                    return standardTimeBases.Length;

                var held = (pauseSample - history.Oldest) / (double)sampleRate;
                var count = 0;
                while (count < standardTimeBases.Length && standardTimeBases[count] <= held)
                    count++;

                return Math.Max(1, count);
            }
        }

        const double ShortestTimeBase = 0.01;
        static readonly double[] TimeBaseSteps = { 1.0, 2.5, 5.0 };
        double[] standardTimeBases = { ShortestTimeBase };

        void RebuildTimeBases(double historySeconds)
        {
            var ladder = new List<double>();
            var nextDecade = true;
            for (var decade = ShortestTimeBase; nextDecade; decade *= 10)
            {
                foreach (var step in TimeBaseSteps)
                {
                    var span = decade * step;
                    if (span > historySeconds && ladder.Count > 0) // NB: ladder.Count > 0 -> shortest span is always offered
                    {
                        nextDecade = false;
                        break;
                    }

                    ladder.Add(span);
                }
            }

            standardTimeBases = ladder.ToArray();
        }

        /// <summary>
        /// The colors channels are drawn in.
        /// </summary>
        public TraceColors Colors { get; }
    }
}
