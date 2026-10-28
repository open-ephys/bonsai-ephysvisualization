using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using OpenCV.Net;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Draws a multi-channel signal as a stack of sweeping traces, each column showing the min and max of
    /// the samples it covers, into whatever region the caller has opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The owner calls <see cref="Update"/> with each block and <see cref="Draw"/> once a frame. Everything
    /// the display can be told is a property, so a control strip is one way to drive it, not part of it. The
    /// settings a node saves are read from and written to that node, which holds the only copy of them.
    /// </para>
    /// <para>
    /// While <see cref="Paused"/>, the display is frozen and can be panned back through a history of recent
    /// samples and redrawn at any timebase.
    /// </para>
    /// <para>
    /// Ported from <c>Bonsai.Ephys.Design.WaveformVisualizer</c>.
    /// </para>
    /// </remarks>
    internal sealed partial class ImGuiWaveformPanel : IDisposable
    {
        readonly WaveformScope settings;
        readonly long historyBytes;
        readonly int maxColumns;

        /// <param name="settings">The node whose display settings the panel reads and writes.</param>
        /// <param name="historyBytes">
        /// Memory for the history of recent samples. The owner sizes it, since only it knows its channel
        /// count and fastest band; the panel works out the seconds it buys once data arrives.
        /// </param>
        /// <param name="maxColumns">
        /// Most columns the decimation buffers hold. Drawing combines columns down to the plot's pixel width,
        /// so more than the widest screen buys nothing.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="historyBytes"/> is not positive, or <paramref name="maxColumns"/> is less than one.
        /// </exception>
        public ImGuiWaveformPanel(WaveformScope settings, long historyBytes, int maxColumns)
        {
            if (historyBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(historyBytes));

            if (maxColumns < 1)
                throw new ArgumentOutOfRangeException(nameof(maxColumns));

            this.settings = settings;
            this.historyBytes = historyBytes;
            this.maxColumns = maxColumns;
            Colors = new(settings);
            frameKeys = FrameKeys();
            tableKeys = TableKeys();
        }

        readonly WaveformCursors cursors = new();

        /// <summary>
        /// Whether the cursors are shown. While they are, a cursor's line can be dragged, and they read the selected
        /// channel.
        /// </summary>
        public bool ShowCursors
        {
            get => cursors.Show;
            set
            {
                // NB: only the cursors the view can no longer reach, as after the record they were placed in has
                // been dropped or cleared, which could not otherwise be shown or centered on. Any other cursor keeps
                // its place however often the cursors are hidden and shown.
                if (value && !cursors.Show)
                {
                    var (first, last) = ReachablePositions;
                    cursors.PlaceUnreachable(first, last, Axis);
                }

                selectOnShow |= value && !cursors.Show;
                cursors.Show = value;
            }
        }

        /// <summary>
        /// The positions the view can show: the frame live, and paused, as far back as the record serves.
        /// </summary>
        (long First, long Last) ReachablePositions =>
            CanPan && history is not null ? (AxisFirst(history), AxisLast) : (window.Start, window.End);

        // NB: once, as the cursors are shown, rather than whenever there is no selection, so that clearing the
        // selection with the cursors shown leaves it clear.
        bool selectOnShow;

        /// <summary>
        /// Moves the cursors to the middle of the view, keeping their spacing, or spreads them evenly across it
        /// if they span more than it does.
        /// </summary>
        public void HomeCursors() => cursors.Home(Axis);

        /// <summary>
        /// Whether channels are drawn as rows of color, a compressed view of every channel at once, rather than
        /// as traces. Rows are laid out alike in both views, so a channel stays in place when the view changes.
        /// </summary>
        public bool ShowHeatmap
        {
            get => !SingleChannel && settings.ShowHeatmap;
            set
            {
                if (!SingleChannel)
                    settings.ShowHeatmap = value;
            }
        }

        /// <summary>
        /// Whether the plot is drawing the heatmap, which it does with the heatmap on and no channel expanded, since an
        /// expanded channel is always drawn as a trace.
        /// </summary>
        public bool HeatmapDrawn => ShowHeatmap && expandedRow < 0;

        /// <summary>
        /// Whether there is only one channel, which is always shown expanded, so that nothing that arranges a stack
        /// of channels applies.
        /// </summary>
        public bool SingleChannel => rowCount == 1;

        /// <summary>
        /// Whether clipping channels are marked and CLIPPING shown.
        /// </summary>
        /// <summary>
        /// Whether the plot is given the whole window, its owner hiding whatever else it draws beside it, such as a
        /// control strip.
        /// </summary>
        public bool Maximized
        {
            get => settings.Maximized;
            set => settings.Maximized = value;
        }

        public bool ShowClipping
        {
            get => settings.ShowClipping;
            set => settings.ShowClipping = value;
        }

        /// <summary>
        /// Magnitude, in the units of the incoming data, at or below which the heatmap shows black. It can
        /// reach the heatmap's range, where the colors are brightest.
        /// </summary>
        public double ColorThreshold
        {
            get => Math.Max(0, Math.Min(settings.ColorThreshold, Math.Max(MinRange, settings.RangeHeatmap)));
            set => settings.ColorThreshold = value;
        }

        /// <summary>
        /// Whether the channel height follows the plot's visible height so that every channel fits it, as far as
        /// the smallest height allows. Setting the height any other way stops it.
        /// </summary>
        public bool FitChannels
        {
            get => settings.FitChannels;
            set
            {
                if (!SingleChannel)
                    settings.FitChannels = value;
            }
        }

        /// <summary>
        /// The channel selected by clicking its trace or label or stepping with Q and E, or -1. The cursors read
        /// it.
        /// </summary>
        int selectedRow = -1;

        Decimator? decimator;
        int sampleRate = 30000;

        WaveformHistory? history;

        // NB: how many rows the data has, which the hidden mask's length used to stand in for.
        int rowCount;

        // NB: the data's rows, and with them whatever is drawn, selected or expanded, are counted in rows. What a
        // user reads and what the settings name are channels, which an owner that reorders its rows maps here.
        RowOrder? order;
        int[]? rowChannels;

        // NB: each row's offset from the top of the lanes, in row heights, or null for row k at k.
        float[]? rowSlots;
        float slotExtent;

        // NB: the row each lane starts at, and the row count last, or null for one lane.
        int[]? laneStarts;

        // NB: the lane given most of the plot's width, the others shrunk to strips, or -1 for equal widths.
        int expandedLane = -1;

        /// <summary>
        /// The channel each row of the data shows, from the top, where each row lies and which lane it is in, or null
        /// for each row showing the channel of its own index, the rows evenly spaced in one lane.
        /// </summary>
        /// <remarks>
        /// A change of channels discards the buffers, whose rows then belong to other channels, and keeps the
        /// selected and the expanded channel where they were. A paused view is read from those buffers, so the
        /// channels are changed only live. A change of positions or lanes alone keeps the buffers.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// The positions or lanes are not one per channel, a lane's rows do not come together, or a position is less
        /// than the one before it in its lane.
        /// </exception>
        public RowOrder? Order
        {
            get => order;
            set
            {
                var (slots, extent, starts) = Arrange(value);
                var channels = value?.Channels;
                if (starts?.Length != laneStarts?.Length)
                    expandedLane = -1;

                order = value;
                rowSlots = slots;
                slotExtent = extent;
                laneStarts = starts;
                if (ReferenceEquals(channels, rowChannels) ||
                    channels is not null && rowChannels is not null && channels.SequenceEqual(rowChannels))
                    return;

                var selected = selectedRow >= 0 ? ChannelOf(selectedRow) : -1;
                var expanded = expandedRow >= 0 ? ChannelOf(expandedRow) : -1;
                rowChannels = channels;
                selectedRow = selected >= 0 ? RowOf(selected) : -1;
                expandedRow = expanded >= 0 ? RowOf(expanded) : -1;
                ResetBuffers();
            }
        }

        static (float[]? Slots, float Extent, int[]? LaneStarts) Arrange(RowOrder? order)
        {
            if (order is null)
                return (null, 0, null);

            var count = order.Channels.Length;
            var positions = order.Positions;
            var lanes = order.Lanes;
            if (positions is not null && positions.Length != count)
                throw new ArgumentException("There must be one position for each channel.", nameof(order));
            if (lanes is not null && lanes.Length != count)
                throw new ArgumentException("There must be one lane for each channel.", nameof(order));

            var starts = new List<int> { 0 };
            var seen = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                if (i > 0 && lanes is not null && lanes[i] != lanes[i - 1])
                    starts.Add(i);
                if (lanes is not null && (i == 0 || lanes[i] != lanes[i - 1]) && !seen.Add(lanes[i]))
                    throw new ArgumentException("A lane's rows must come together.", nameof(order));
            }
            starts.Add(count);
            var laneStarts = starts.Count > 2 ? starts.ToArray() : null;
            if (positions is null && laneStarts is null)
                return (null, 0, null);

            bool SameLane(int i) => lanes is null || lanes[i] == lanes[i - 1];

            var slots = new float[count];
            if (positions is null)
            {
                for (int lane = 0; lane < starts.Count - 1; lane++)
                    for (int i = starts[lane]; i < starts[lane + 1]; i++)
                        slots[i] = i - starts[lane];
            }
            else
            {
                var pitch = float.PositiveInfinity;
                var top = count > 0 ? positions[0] : 0;
                for (int i = 0; i < count; i++)
                {
                    top = Math.Min(top, positions[i]);
                    if (i == 0 || !SameLane(i))
                        continue;

                    var step = positions[i] - positions[i - 1];
                    if (step < 0)
                        throw new ArgumentException("Each position must be at least the one before it in its lane.",
                            nameof(order));
                    if (step > 0)
                        pitch = Math.Min(pitch, step);
                }

                if (float.IsPositiveInfinity(pitch))
                    pitch = 1;

                for (int i = 0; i < count; i++)
                    slots[i] = (positions[i] - top) / pitch;
            }

            var extent = 0f;
            for (int lane = 0; lane < starts.Count - 1; lane++)
            {
                if (starts[lane + 1] > starts[lane])
                    extent = Math.Max(extent, slots[starts[lane + 1] - 1] + 1);
            }

            return (slots, extent, laneStarts);
        }

        // NB: positions and lanes apply only when the data has a row for each, which a block posted before a new
        // order may not.
        float[]? SlotsFor(int rows) => rowSlots is { } slots && slots.Length == rows ? slots : null;

        int[]? LanesFor(int rows) => laneStarts is { } starts && starts[starts.Length - 1] == rows ? starts : null;

        float SlotOf(int row) => RowLayout.SlotOf(SlotsFor(rowCount), row);

        // NB: the tallest lane's span in row heights, the row count when evenly spaced in one lane.
        float SlotCount => SlotsFor(rowCount) is not null ? slotExtent : rowCount;

        internal int ChannelOf(int row) => ChannelOf(rowChannels, row);

        internal static int ChannelOf(int[]? rowChannels, int row) =>
            rowChannels is { } map && (uint)row < (uint)map.Length ? map[row] : row;

        int RowOf(int channel) => rowChannels is { } map ? Array.IndexOf(map, channel) : channel;

        // NB: the node's hidden channels as a mask by row, for looking a row up, worked out again whenever the node's
        // array or the row order is replaced or the channel count changes. Never written to: hiding and showing
        // replace the array.
        bool[] hiddenMask = Array.Empty<bool>();
        int[]? maskedFrom;
        int[]? maskedRows;

        bool[] RowHidden
        {
            get
            {
                if (hiddenMask.Length != rowCount || !ReferenceEquals(maskedFrom, settings.HiddenChannels) ||
                    !ReferenceEquals(maskedRows, rowChannels))
                {
                    maskedFrom = settings.HiddenChannels;
                    maskedRows = rowChannels;
                    hiddenMask = new bool[rowCount];
                    var hidden = new HashSet<int>(maskedFrom ?? Array.Empty<int>());
                    for (int row = 0; row < rowCount; row++)
                        hiddenMask[row] = hidden.Contains(ChannelOf(row));
                }

                return hiddenMask;
            }
        }

        // NB: the hidden channels when a drag on the labels began, which the drag sets its range against.
        int[] dragOriginal = Array.Empty<int>();

        /// <summary>
        /// How long a channel is shown clipping after it last clipped.
        /// </summary>
        const double ClipHoldSeconds = 1;

        /// <summary>
        /// How often the clipping marks flash, on and off.
        /// </summary>
        const double ClipFlashHz = 4;

        // NB: seconds of data shown, which stand still while paused or while no data arrives, and so hold the marks.
        double dataSeconds;
        double[] lastClip = Array.Empty<double>();

        /// <summary>
        /// Marks the rows set in <paramref name="rows"/> as clipping now: a sample sat at either end of
        /// their source's range. Ignored while paused, which keeps the marks as they were.
        /// </summary>
        public void MarkClipping(bool[] rows)
        {
            if (Paused || rows.Length != lastClip.Length)
                return;

            for (int c = 0; c < rows.Length; c++)
            {
                if (rows[c])
                    lastClip[c] = dataSeconds;
            }
        }

        /// <summary>
        /// Forgets every channel's clipping, as when what is displayed comes from another source.
        /// </summary>
        public void ClearClipping()
        {
            for (int c = 0; c < lastClip.Length; c++)
                lastClip[c] = double.NegativeInfinity;
        }

        bool Clipping(int row) =>
            ShowClipping && !RowHidden[row] && dataSeconds - lastClip[row] < ClipHoldSeconds;

        // NB: steady while paused, when the marks only say which channels were clipping and flashing would only
        // distract from studying the view.
        bool ClipFlashOn => Paused || (long)(ImGui.GetTime() * 2 * ClipFlashHz) % 2 == 0;

        int expandedRow = -1;

        /// <summary>
        /// Most an expanded channel's amplitude can be magnified, against the range.
        /// </summary>
        const double MaxAmplitudeZoom = 1000;

        // NB: for one channel at one range, so that expanding another, collapsing or changing the range starts it over.
        (int Row, double Range, double Center, double Span) amplitudeView = (-1, 0, 0, 0);

        /// <summary>
        /// The value at the middle of a row and the amplitude a row spans: an expanded channel's zoom, or zero and the
        /// range.
        /// </summary>
        (double Center, double Span) AmplitudeView =>
            expandedRow >= 0 && amplitudeView.Row == expandedRow && amplitudeView.Range == Range
                ? (amplitudeView.Center, amplitudeView.Span)
                : (0, Range);

        /// <summary>
        /// Zooms the expanded channel to <paramref name="span"/>, no wider than the range, keeping
        /// <paramref name="value"/> a <paramref name="fraction"/> of the row above its middle.
        /// </summary>
        void SetAmplitudeView(double value, double fraction, double span)
        {
            span = Math.Max(Range / MaxAmplitudeZoom, Math.Min(Range, span));
            amplitudeView = (expandedRow, Range, value - fraction * span, span);
        }

        /// <summary>
        /// Seconds of data spanned by the display, no more than the history holds, of which a paused view can show
        /// only what was held at pausing.
        /// </summary>
        public double Timebase
        {
            get => history is null ? settings.Timebase : Math.Min(settings.Timebase, standardTimeBases[ServableTimeBases - 1]);
            set => SetTimebaseAt(value, plotLeft + plotSpan / 2);
        }

        /// <summary>
        /// Height in pixels of one channel's row, which may be a fraction of a pixel, exactly the fit height while
        /// fitting and never less than it. Setting it stops fitting.
        /// </summary>
        public float ChannelHeight
        {
            get => FitChannels ? FitHeight : Math.Max(settings.ChannelHeight, FitHeight);
            set
            {
                settings.ChannelHeight = value;
                FitChannels = false;
            }
        }

        /// <summary>
        /// The channel height at which every channel just fills the plot's visible height, as far as the smallest
        /// height allows. Rows any shorter would leave part of the plot unused.
        /// </summary>
        /// <remarks>
        /// Positioned rows may fit below the smallest height, since a probe whose electrodes share depths can need
        /// several rows to a pixel to be seen whole.
        /// </remarks>
        float FitHeight => rowCount > 0 && visibleHeight > 0
            ? SlotsFor(rowCount) is not null
                ? visibleHeight / SlotCount
                : Math.Max(MinChannelHeight, visibleHeight / SlotCount)
            : MinChannelHeight;

        /// <summary>
        /// The next multiple of the channel height step above <paramref name="height"/>, or below it when
        /// <paramref name="direction"/> is negative.
        /// </summary>
        /// <remarks>
        /// To a multiple rather than by a step, so that a height off the multiples, as a fitted one is, comes back
        /// onto them.
        /// </remarks>
        public static float StepChannelHeight(float height, int direction) => direction > 0
            ? (MathF.Floor(height / ChannelHeightStep) + 1) * ChannelHeightStep
            : (MathF.Ceiling(height / ChannelHeightStep) - 1) * ChannelHeightStep;

        /// <summary>
        /// Amplitude spanned by one channel's row, in the units of the incoming data. This is the heatmap's range
        /// while the heatmap is drawn and the traces' range otherwise, so every control that sets it changes the
        /// range of what is on screen.
        /// </summary>
        public double Range
        {
            get => Math.Max(MinRange, HeatmapDrawn ? settings.RangeHeatmap : settings.RangeAmplitude);
            set
            {
                if (HeatmapDrawn)
                    settings.RangeHeatmap = value;
                else
                    settings.RangeAmplitude = value;
            }
        }

        /// <summary>
        /// Refresh rate, in Hz, of the display the panel is drawn on, which decides how fast a sweep can be shown
        /// as it is written. The measured frame rate will not do, since it drops whenever a frame is slow.
        /// </summary>
        public double RefreshRate { get; set; } = 60;

        /// <summary>
        /// Unit of the incoming data, shown wherever an amplitude is.
        /// </summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>
        /// Whether the display is frozen. While it is, the view can be panned through the history and redrawn
        /// at any timebase.
        /// </summary>
        public bool Paused
        {
            get => pauseSample >= 0;
            set
            {
                if (value == Paused) return;
                if (value) Pause();
                else Resume();
            }
        }

        /// <summary>
        /// Supplies one block of samples. While paused, a block whose rate or channel count would clear the
        /// history is dropped.
        /// </summary>
        /// <param name="data">Channel-by-sample matrix of 32-bit floats, one row per channel.</param>
        /// <param name="dataSampleRate">Sample rate of the incoming data, in Hz.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="dataSampleRate"/> is less than one.
        /// </exception>
        public void Update(Mat data, int dataSampleRate)
        {
            if (dataSampleRate < 1)
                throw new ArgumentOutOfRangeException(nameof(dataSampleRate));

            var budgetSamples = historyBytes / (data.Rows * sizeof(float));
            var capacity = (int)Math.Max(1, Math.Min(int.MaxValue, budgetSamples));
            var historyReplaced = history is null || history.Rows != data.Rows || history.Capacity != capacity;

            // NB: the history converts sample offsets to time with a single factor, so it cannot hold two
            // rates. A change of band that keeps the rate is left alone: what the history holds is what was
            // displayed, seams between band switches are included.
            var historyInvalid = historyReplaced || dataSampleRate != sampleRate;

            // NB: a paused view is read from the history, so a block that would clear it is dropped. Once
            // the view resumes, the next block still differs and is applied as usual.
            if (Paused && historyInvalid)
                return;

            sampleRate = dataSampleRate;
            if (history is null || historyReplaced)
            {
                history?.Dispose();
                history = new WaveformHistory(data.Rows, capacity);
            }

            if (historyInvalid)
            {
                history.Clear();
                RebuildTimeBases(capacity / (double)sampleRate);
            }

            RebuildBuffers(data.Rows);
            decimator.Process(data);

            if (!Paused)
            {
                history.Write(data);
                dataSeconds += data.Cols / (double)sampleRate;
            }
        }

        /// <summary>
        /// Rebuilds the decimation buffers if the channel count or the timebase no longer matches them, and
        /// moves the window onto the new column spacing.
        /// </summary>
        [MemberNotNull(nameof(decimator), nameof(pannedDecimator))]
        void RebuildBuffers(int rows)
        {
            var totalSamples = Math.Max(1, (int)(Timebase * sampleRate));
            var samplesPerBin = (totalSamples + maxColumns - 1) / maxColumns;
            var columns = totalSamples / samplesPerBin;

            if (decimator is null ||
                pannedDecimator is null ||
                decimator.Sweep.Rows != rows ||             // # channels
                decimator.Sweep.Cols != columns ||          // # downsamples
                decimator.DownsampleFactor != samplesPerBin) // factor to map totalSamples -> # downsamples
            {
                decimator?.Dispose();
                pannedDecimator?.Dispose();
                decimator = new Decimator(rows, columns, samplesPerBin);

                // NB: the paused view reduces out of the history into its own decimator, at the same width as
                // the live one, so that changing the timebase while paused rebuilds both together.
                pannedDecimator = new Decimator(rows, columns, samplesPerBin);

                if (rowCount != rows)
                {
                    // NB: set outright rather than through Expand and Collapse, which move the scroll, and this can
                    // run outside a frame, from the owner's drain timer.
                    if (rows == 1)
                        expandedRow = 0;
                    else if (rowCount == 1)
                        expandedRow = -1;

                    rowCount = rows;
                    lastClip = new double[rows];
                    ClearClipping();
                }

                RebuildWindow(samplesPerBin, columns);
            }

            // NB: also when only the anchor is waiting, so that a timebase change which happens to land on
            // the same column spacing still moves the window to where the gesture asked.
            else if (anchorPending)
                RebuildWindow(samplesPerBin, columns);
        }

        /// <summary>
        /// Draws the waveform stack into the region the caller has opened. Called once per frame.
        /// </summary>
        public void Draw()
        {
            // NB: ahead of the envelope, which resuming with Space disposes. Answering it with the other
            // gestures would free the matrices the plot is about to read.
            AnswerFrameKeys();

            // NB: also here and not only in Update, so a timebase change takes effect while no data is
            // arriving, as when paused after acquisition has stopped.
            if (decimator is not null)
                RebuildBuffers(decimator.Sweep.Rows);

            if (decimator is not null)
            {
                // NB: no vertical padding, so the plot frame lands on the region's own edges and can
                // be lined up with whatever the owner puts beside it.
                var padding = ImGui.GetStyle().WindowPadding;
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding.X, 0));
                ImGui.BeginChild("##data", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoNavInputs);
                var (envelope, shown) = DisplayEnvelope(decimator);
                WaveformPlot(envelope, shown);
                ImGui.EndChild();
                ImGui.PopStyleVar();
            }
        }

        /// <summary>
        /// Discards the decimation buffers so the next <see cref="Update"/> rebuilds them.
        /// </summary>
        /// <remarks>
        /// The decimators carry reduction state across calls, so anything that changes the meaning
        /// of the incoming samples has to discard them rather than let a partially filled output bin
        /// be completed from a different signal.
        /// </remarks>
        public void ResetBuffers()
        {
            decimator?.Dispose();
            history?.Dispose();
            DisposeHistoryView();
            decimator = null;
            history = null;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            ResetBuffers();
        }
    }
}
