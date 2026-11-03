using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;

namespace OpenEphys.EphysVisualization.Design
{
    partial class ImGuiWaveformPanel
    {
        /// <summary>
        /// How much of a window, or of the channels on screen, a coarse step covers.
        /// </summary>
        const float CoarsePanFraction = 0.8f;

        /// <summary>
        /// Natural log of the factor the range changes by per pixel of Alt+Drag: it halves every 140 or so upward.
        /// </summary>
        const double RangeDragRate = 0.005;

        /// <summary>
        /// Natural log of the factor the timebase changes by per pixel of Ctrl+Drag: it halves every 140 or so
        /// rightward.
        /// </summary>
        const double TimebaseDragRate = 0.005;

        /// <summary>
        /// Factor an expanded channel's amplitude is magnified by per notch of Ctrl+Wheel.
        /// </summary>
        const double AmplitudeWheelFactor = 1.25;

        bool? dragHidden;
        int dragAnchor;

        // While a drag is held, the value it began from and where the pointer was then.
        (double Value, float Y)? rangeDrag;
        (float Value, float Y, float Pivot, float Scroll)? heightDrag;
        (double Span, float Y, double Value, double Fraction)? amplitudeDrag;
        (double Value, float X, long Anchor, double Fraction)? timebaseDrag;
        bool grabbing;

        readonly DragBox zoomBox = new(ImGuiMouseButton.Right);

        // NB: wheel movement not yet a whole step, which a touchpad sends in fractions of a notch.
        float wheelSteps;

        float collapsedScroll;
        float? pendingScroll;

        static int HoveredRow(Lane[] lanes, int lane)
        {
            var layout = lanes[lane].Rows;
            var mouse = ImGui.GetMousePos();
            var left = lanes[lane].LabelLeft;
            var right = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X;
            if (ImGui.GetScrollMaxY() > 0)
                right -= ImGui.GetStyle().ScrollbarSize;
            for (int next = lane + 1; next < lanes.Length; next++)
            {
                if (!lanes[next].Strip)
                {
                    right = lanes[lane].Left + lanes[lane].Width;
                    break;
                }
            }

            if (!ImGui.IsWindowHovered() ||
                mouse.X < left || mouse.X >= right ||
                mouse.Y < layout.Top || mouse.Y >= layout.Bottom)
            {
                return -1;
            }

            var row = layout.RowAt(mouse.Y);
            return row >= layout.FirstRow && row < layout.LastRow ? row : -1;
        }

        // NB: the scroll clamps to the rows as they were the frame before, so a scroll for rows that change is set
        // once they have.
        void ScrollIfPending()
        {
            if (pendingScroll is float scroll)
            {
                ImGui.SetScrollY(scroll);
                pendingScroll = null;
            }
        }

        void HandleChannelInput(int row)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                dragHidden = null;
            if (row < 0)
                return;

            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                if (expandedRow >= 0) Collapse();
                else Expand(row);
                return;
            }

            if (expandedRow >= 0)
                return;

            var onLabels = ImGui.GetMousePos().X < plotLeft;
            if (onLabels && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && (Modifiers() || Modifiers(shift: true)))
            {
                dragOriginal = ChannelSettings.HiddenOf(settings.Channels);
                dragHidden = ImGui.GetIO().KeyShift;
                dragAnchor = row;
                if (!ImGui.GetIO().KeyShift)
                    selectedRow = row;
            }

            if (dragHidden is bool hide)
            {
                var lo = Math.Min(dragAnchor, row);
                var hi = Math.Max(dragAnchor, row);
                var dragged = new HashSet<int>();
                for (int r = lo; r <= hi; r++)
                    dragged.Add(ChannelOf(r));

                var hidden = dragOriginal.Where(c => !dragged.Contains(c)).ToList();
                if (hide)
                {
                    hidden.AddRange(dragged);
                    hidden.Sort();
                }

                if (!hidden.SequenceEqual(ChannelSettings.HiddenOf(settings.Channels)))
                    settings.Channels = ChannelSettings.WithHidden(settings.Channels, hidden);

                if (selectedRow >= 0 && RowHidden[selectedRow])
                    selectedRow = -1;
            }
        }

        readonly (ImGuiKey Key, bool Repeat, Action Answer)[] frameKeys;
        readonly (ImGuiKey Key, bool Repeat, Action<RowLayout> Answer)[] tableKeys;

        (ImGuiKey, bool, Action)[] FrameKeys() => new (ImGuiKey, bool, Action)[]
        {
            (ImGuiKey.Space, false, () => Paused = !Paused),
            (ImGuiKey.C, false, () => ShowCursors = !ShowCursors),
            (ImGuiKey.H, false, () => ShowHeatmap = !ShowHeatmap),
            (ImGuiKey.F, false, () => FitChannels = !FitChannels),
            (ImGuiKey.T, false, LongestTimebase),
            (ImGuiKey.M, false, () => Maximized = !Maximized),
            (ImGuiKey.Escape, false, Escape),
            (ImGuiKey.A, true, () => TimeKey(-1)),
            (ImGuiKey.D, true, () => TimeKey(1)),
            (ImGuiKey.LeftArrow, true, () => CursorKey(-1)),
            (ImGuiKey.RightArrow, true, () => CursorKey(1)),
            (ImGuiKey.Equal, false, AddCursorKey),
            (ImGuiKey.KeypadAdd, false, AddCursorKey),
            (ImGuiKey.Minus, false, RemoveCursorKey),
            (ImGuiKey.KeypadSubtract, false, RemoveCursorKey),
            (ImGuiKey.Delete, false, RemoveCursorKey),
        };

        // NB: these scroll the channels, which ImGui does to the window current when asked, so they are answered
        // inside the table that owns the scroll.
        (ImGuiKey, bool, Action<RowLayout>)[] TableKeys() => new (ImGuiKey, bool, Action<RowLayout>)[]
        {
            (ImGuiKey.W, true, layout => ChannelKey(-1, layout)),
            (ImGuiKey.S, true, layout => ChannelKey(1, layout)),
            (ImGuiKey.Q, true, layout => { if (Modifiers()) StepSelection(-1, layout); }),
            (ImGuiKey.E, true, layout => { if (Modifiers()) StepSelection(1, layout); }),
            (ImGuiKey.L, false, LaneKey),
            (ImGuiKey.X, false, ToggleExpanded),
        };

        void ToggleExpanded(RowLayout layout)
        {
            if (selectedRow < 0 && expandedRow < 0)
                selectedRow = FirstShownInView(layout);
            if (expandedRow >= 0) Collapse();
            else if (selectedRow >= 0) Expand(selectedRow);
        }

        /// <summary>
        /// Every key the panel answers, for a host to hand to whichever panel is under the pointer.
        /// </summary>
        public IEnumerable<ImGuiKey> Hotkeys => frameKeys.Select(k => k.Key).Concat(tableKeys.Select(k => k.Key));

        /// <summary>
        /// Whether the pointer is over a pane beside the panel that answers keys of its own, which the panel then
        /// leaves to it, all but Q, E and X. Set by the host each frame, before the panel is drawn.
        /// </summary>
        public bool KeysElsewhere { get; set; }

        static readonly ImGuiKey[] SelectionKeys = { ImGuiKey.Q, ImGuiKey.E, ImGuiKey.X };

        void AnswerFrameKeys()
        {
            foreach (var key in frameKeys)
            {
                if (Pressed(key.Key, key.Repeat))
                    key.Answer();
            }
        }

        void AnswerTableKeys(in RowLayout layout)
        {
            foreach (var key in tableKeys)
            {
                if (Pressed(key.Key, key.Repeat, anywhere: Array.IndexOf(SelectionKeys, key.Key) >= 0))
                    key.Answer(layout);
            }
        }

        bool Pressed(ImGuiKey key, bool repeat, bool anywhere = false) =>
            (anywhere || !KeysElsewhere) && !ImGui.GetIO().WantTextInput && ImGui.IsKeyPressed(key, repeat);

        /// <summary>
        /// Whether exactly the named modifiers are down and the rest are up.
        /// </summary>
        internal static bool Modifiers(bool ctrl = false, bool shift = false, bool alt = false)
        {
            var io = ImGui.GetIO();
            return io.KeyCtrl == ctrl && io.KeyShift == shift && io.KeyAlt == alt && !io.KeySuper;
        }

        // NB: ImGui doesn't scroll the ##rows window (see WaveformPlot), so every wheel gesture over the rows ends up
        // here. A touchpad pinch arrives as Ctrl+wheel.
        void HandleWheel(in RowLayout layout)
        {
            var io = ImGui.GetIO();
            var wheel = io.MouseWheel;
            var sideways = io.MouseWheelH;
            var mouse = ImGui.GetMousePos();
            if (wheel == 0 && sideways == 0 || !ImGui.IsWindowHovered())
                return;

            if (Modifiers())
            {
                if (expandedRow >= 0)
                {
                    var (center, span) = AmplitudeView;
                    SetAmplitudeView(center + wheel * span / AmplitudeDivisions, 0, span);
                }
                else
                {
                    var step = MathF.Floor(Math.Min(5 * ImGui.GetFontSize(), visibleHeight * 0.67f));
                    ImGui.SetScrollY(ImGui.GetScrollY() - wheel * step);
                }
                if (sideways != 0)
                    Pan((long)(sideways * (window.Span / TimeDivisions)));
                return;
            }

            if (Modifiers(shift: true))
            {
                Pan((long)(wheel * (window.Span / TimeDivisions)));
                return;
            }

            if (wheel == 0 || !OverPlot(mouse.X))
                return;

            wheelSteps += wheel;
            var steps = (int)wheelSteps;
            wheelSteps -= steps;
            for (var direction = Math.Sign(steps); steps != 0; steps -= direction)
            {
                if (Modifiers(ctrl: true, shift: true))
                    StepTimebase(-direction, mouse.X);
                else if (Modifiers(alt: true))
                    StepRange(-direction);
                else if (Modifiers(ctrl: true) && expandedRow < 0 && heightDrag is null)
                {
                    var height = ChannelHeight;
                    var pivot = (mouse.Y - layout.Origin) / layout.RowHeight;
                    SetChannelHeight(StepChannelHeight(height, direction), height, pivot, ImGui.GetScrollY(), layout);
                }
                else if (Modifiers(ctrl: true) && expandedRow >= 0 && amplitudeDrag is null)
                {
                    var (center, span) = AmplitudeView;
                    var fraction = RowFraction(layout, mouse.Y);
                    SetAmplitudeView(center + fraction * span, fraction,
                        PlotText.Significant(span * Math.Pow(AmplitudeWheelFactor, -direction)));
                }
            }
        }

        /// <summary>
        /// W and S: alone or with Shift, scroll the channels; with Ctrl, step the channel height; with Alt, step the
        /// range. <paramref name="direction"/> is -1 for W, upward, which magnifies, as the wheel's upward does.
        /// </summary>
        void ChannelKey(int direction, in RowLayout layout)
        {
            if (Modifiers() || Modifiers(shift: true))
                ImGui.SetScrollY(ImGui.GetScrollY() + direction * ChannelStep(layout));
            else if (Modifiers(alt: true))
                StepRange(direction);
            else if (Modifiers(ctrl: true) && expandedRow < 0)
            {
                var height = ChannelHeight;
                var pivot = ((layout.Top + layout.Bottom) / 2 - layout.Origin) / layout.RowHeight;
                SetChannelHeight(StepChannelHeight(height, -direction), height, pivot, ImGui.GetScrollY(), layout);
            }
        }

        /// <summary>
        /// A and D: alone or with Shift, move through time; with Ctrl, step the timebase about the middle of the
        /// plot. <paramref name="direction"/> is 1 for D, rightward, which magnifies, as a rightward Ctrl+Drag does.
        /// </summary>
        void TimeKey(int direction)
        {
            if (Modifiers() || Modifiers(shift: true))
                Pan(-direction * PanStep());
            else if (Modifiers(ctrl: true))
                StepTimebase(-direction, plotLeft + plotSpan / 2);
        }

        void Escape()
        {
            if (zoomBox.Dragging) zoomBox.Cancel();
            else selectedRow = -1;
        }

        void CursorKey(int direction)
        {
            var cells = Modifiers() ? 1 : Modifiers(shift: true) ? CursorCoarseStep : 0;
            if (cells > 0)
                cursors.Step(direction * cells, Axis);
        }

        // NB: with or without Shift, since + shares its key with = and typing it takes Shift on most layouts.
        void AddCursorKey()
        {
            if (Modifiers() || Modifiers(shift: true))
                cursors.Add(Axis);
        }

        void RemoveCursorKey()
        {
            if (Modifiers() || Modifiers(shift: true))
                cursors.RemoveActive();
        }

        const int CursorCoarseStep = 10;

        bool OverPlot(float x) => x >= plotLeft && x < plotLeft + plotSpan;

        void HandleDrags(in RowLayout layout)
        {
            var mouse = ImGui.GetMousePos();
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                (rangeDrag, heightDrag, timebaseDrag, amplitudeDrag) = (null, null, null, null);

            if (!ImGui.IsMouseDown(ImGuiMouseButton.Middle))
                grabbing = false;

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle) && ImGui.IsWindowHovered() && Modifiers())
                grabbing = true;

            if (grabbing && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
            {
                var delta = ImGui.GetIO().MouseDelta;
                if (expandedRow >= 0)
                {
                    var (center, span) = AmplitudeView;
                    SetAmplitudeView(center + delta.Y / layout.RowHeight * span, 0, span);
                }
                else
                    ImGui.SetScrollY(ImGui.GetScrollY() - delta.Y);
                if (plotSpan > 0)
                    Pan((long)(delta.X * (window.Span / plotSpan)));
            }

            var clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered() && OverPlot(mouse.X);

            if (clicked && Modifiers(alt: true))
                rangeDrag = (Range, mouse.Y);

            if (rangeDrag is { } range)
                Range = PlotText.Significant(range.Value * Math.Exp(Beyond(mouse.Y - range.Y) * RangeDragRate));

            if (clicked && Paused && Modifiers(ctrl: true))
            {
                var anchor = Axis.PositionAt(mouse.X);
                timebaseDrag = (Timebase, mouse.X, anchor, window.FractionOf(anchor));
                if (expandedRow < 0)
                    heightDrag = (ChannelHeight, mouse.Y, (mouse.Y - layout.Origin) / layout.RowHeight, ImGui.GetScrollY());
                else
                {
                    var (center, span) = AmplitudeView;
                    var fraction = RowFraction(layout, mouse.Y);
                    amplitudeDrag = (span, mouse.Y, center + fraction * span, fraction);
                }
            }

            // NB: rounded, which also keeps the decimators from being rebuilt on every pixel of the drag.
            if (timebaseDrag is { } zoom)
            {
                var longest = standardTimeBases[ServableTimeBases - 1];
                var value = PlotText.Significant(zoom.Value * Math.Exp(-Beyond(mouse.X - zoom.X) * TimebaseDragRate));
                SetTimebase(Math.Max(ShortestTimeBase, Math.Min(longest, value)), zoom.Anchor, zoom.Fraction);
            }

            if (amplitudeDrag is { } amplitude && expandedRow >= 0)
            {
                var span = PlotText.Significant(amplitude.Span * Math.Exp(Beyond(mouse.Y - amplitude.Y) * RangeDragRate));
                SetAmplitudeView(amplitude.Value, amplitude.Fraction, span);
            }

            if (heightDrag is { } height && expandedRow < 0)
            {
                var delta = (int)Math.Round(-0.2f * Beyond(mouse.Y - height.Y));
                if (height.Value > 100)
                    delta *= 3;
                SetChannelHeight(height.Value + delta, height.Value, height.Pivot, height.Scroll, layout);
            }
        }

        /// <summary>
        /// Right click and drag a zoom box. A double-click zooms all the way out.
        /// </summary>
        void HandleZoomBox(in RowLayout layout)
        {
            var mouse = ImGui.GetMousePos();
            var onPlot = ImGui.IsWindowHovered() && OverPlot(mouse.X) && Modifiers();
            if (onPlot && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Right))
            {
                zoomBox.Cancel();
                ZoomAll();
            }
            else if (onPlot && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                zoomBox.Begin();

            if (!zoomBox.Released(PlotMin(layout.Top), PlotMax(layout.Bottom), out var box))
                return;

            if (box.Wide && Paused)
            {
                var longest = standardTimeBases[ServableTimeBases - 1];
                var value = PlotText.Significant(Timebase * (box.Max.X - box.Min.X) / plotSpan);
                SetTimebase(Math.Max(ShortestTimeBase, Math.Min(longest, value)), Axis.PositionAt(box.Min.X), 0);
            }

            if (box.Tall && expandedRow >= 0)
            {
                var (center, span) = AmplitudeView;
                SetAmplitudeView(center + RowFraction(layout, (box.Min.Y + box.Max.Y) / 2) * span, 0,
                    PlotText.Significant(span * (box.Max.Y - box.Min.Y) / layout.RowHeight));
            }
            else if (box.Tall)
            {
                var slots = (int)Math.Ceiling(layout.Slot(layout.LastRow - 1) + 1);
                var first = Math.Max(0, (int)Math.Floor(layout.SlotAt(box.Min.Y)));
                var last = Math.Min(slots - 1, (int)Math.Floor(layout.SlotAt(box.Max.Y - 1)));
                ChannelHeight = Math.Min(layout.Bottom - layout.Top, MathF.Floor(visibleHeight / (last - first + 1)));
                pendingScroll = first * ChannelHeight;
            }
        }

        Vector2 PlotMin(float top) => new(plotLeft, top);
        Vector2 PlotMax(float bottom) => new(plotLeft + plotSpan, bottom);

        /// <summary>
        /// Shows everything: the longest timebase, every channel fitted to the plot, and an expanded channel's
        /// whole range.
        /// </summary>
        void ZoomAll()
        {
            LongestTimebase();
            if (!FitChannels)
            {
                ChannelHeight = FitHeight;
                pendingScroll = 0;
            }
            amplitudeView.Row = -1;
        }

        void LongestTimebase() => SetTimebaseAt(standardTimeBases[ServableTimeBases - 1], plotLeft + plotSpan / 2);

        /// <summary>
        /// How far <paramref name="y"/> lies above the middle of the expanded channel's row, in rows.
        /// </summary>
        float RowFraction(in RowLayout layout, float y) =>
            (layout.RowTop(expandedRow) + layout.RowHeight / 2 - y) / layout.RowHeight;

        // How far a drag has gone along one axis past the drag threshold.
        static float Beyond(float distance)
        {
            var threshold = ImGui.GetIO().MouseDragThreshold;
            return Math.Sign(distance) * Math.Max(0, Math.Abs(distance) - threshold);
        }

        void SetChannelHeight(float height, float baseHeight, float pivot, float baseScroll, in RowLayout layout)
        {
            // NB: a drag that has not moved yet leaves a fitted height fitted.
            if (height == baseHeight)
                return;

            ChannelHeight = Math.Min(layout.Bottom - layout.Top, height);
            ImGui.SetScrollY(baseScroll + pivot * (ChannelHeight - baseHeight));
        }

        void StepTimebase(int direction, float x)
        {
            var offered = ServableTimeBases;
            var i = Array.BinarySearch(standardTimeBases, 0, offered, Timebase);
            if (i < 0)
            {
                i = ~i;
                if (direction < 0)
                    i--;
            }
            else
            {
                i += direction;
            }

            var stepped = standardTimeBases[Math.Max(0, Math.Min(offered - 1, i))];
            SetTimebaseAt(stepped, x);
        }

        void SetTimebaseAt(double value, float x)
        {
            var anchor = Axis.PositionAt(x);
            SetTimebase(value, anchor, window.FractionOf(anchor));
        }

        void StepRange(int direction)
        {
            var (i, exact) = RangeIndex(Range);
            if (exact || direction > 0)
                i += direction;

            Range = RangeAt(i);
        }

        static float ChannelStep(in RowLayout layout)
        {
            var slots = (int)Math.Ceiling(layout.Slot(layout.LastRow - 1) + 1);
            var visibleRows = Math.Max(1,
                Math.Min(slots, (int)Math.Ceiling(layout.SlotAt(layout.Bottom))) -
                Math.Max(0, (int)Math.Floor(layout.SlotAt(layout.Top))));
            return Modifiers(shift: true)
                ? (int)(visibleRows * CoarsePanFraction) * layout.RowHeight
                : layout.RowHeight;
        }

        long PanStep() => Modifiers(shift: true)
            ? (long)(window.Span * CoarsePanFraction)
            : window.Span / TimeDivisions;

        /// <summary>
        /// Selects the next shown channel in <paramref name="direction"/>, and brings it into view: expanded in
        /// its turn if a channel is expanded, or scrolled to otherwise.
        /// </summary>
        void StepSelection(int direction, in RowLayout layout)
        {
            var from = expandedRow >= 0 ? expandedRow
                : selectedRow >= 0 ? selectedRow
                : layout.FirstVisible - direction;
            for (var c = from + direction; c >= 0 && c < rowCount; c += direction)
            {
                if (!RowHidden[c])
                {
                    selectedRow = c;
                    break;
                }
            }

            if (selectedRow < 0)
                return;

            if (expandedRow >= 0)
            {
                expandedRow = selectedRow;
                return;
            }

            var top = layout.RowTop(selectedRow);
            if (top < layout.Top)
                ImGui.SetScrollY(ImGui.GetScrollY() - (layout.Top - top));
            else if (top + layout.RowHeight > layout.Bottom)
                ImGui.SetScrollY(ImGui.GetScrollY() + (top + layout.RowHeight - layout.Bottom));
        }

        /// <summary>
        /// The first channel in view that is not hidden, or -1, which is what anything needing a selection
        /// selects when there is none.
        /// </summary>
        int FirstShownInView(in RowLayout layout)
        {
            for (int c = layout.FirstVisible; c < layout.LastVisible; c++)
            {
                if (!RowHidden[c])
                    return c;
            }
            return -1;
        }

        void Expand(int row)
        {
            collapsedScroll = ImGui.GetScrollY();
            expandedRow = row;
        }

        // NB: Q and E may have stepped the expanded channel away from the one the expand began on.
        void Collapse()
        {
            if (SingleChannel)
                return;

            var height = ChannelHeight;
            var rowTop = SlotOf(expandedRow) * height;
            if (rowTop < collapsedScroll)
                collapsedScroll = rowTop;
            else if (rowTop + height > collapsedScroll + visibleHeight)
                collapsedScroll = rowTop + height - visibleHeight;

            expandedRow = -1;
            pendingScroll = collapsedScroll;
        }
    }
}
