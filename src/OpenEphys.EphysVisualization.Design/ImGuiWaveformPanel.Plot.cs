using System;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.Utilities.Text;
using OpenCV.Net;

namespace OpenEphys.EphysVisualization.Design
{
    partial class ImGuiWaveformPanel
    {
        const int TimeDivisions = 10;

        // visual constants
        static readonly uint ColSweepCursor = ImGuiPalette.Yellow;
        const float SweepCursorWeight = 1;
        static readonly uint ColGraticule = ImGuiPalette.Grey0x88;
        const float GraticuleWeight = 1;
        const int AmplitudeDivisions = 10;
        static readonly uint ColDivision = ImGuiPalette.Grey0x55;
        const float DivisionDotGap = 3;
        const float HoverLineWeight = 3;
        static readonly uint ColHoveredRow = ImGuiPalette.Grey0x99;
        static readonly uint ColClipping = ImGuiPalette.Red;
        static readonly uint ColSelectedRow = ImGuiPalette.WithAlpha(ImGuiPalette.White, 0x20);

        static readonly uint ColFrozenTail = ImGuiPalette.WithAlpha(ImGuiPalette.Black, 0x80);
        internal static readonly uint ColZoomBox = ImGuiPalette.Grey0x99;
        internal static readonly uint ColZoomBoxFill = ImGuiPalette.WithAlpha(ImGuiPalette.White, 0x18);

        /// <summary>
        /// Color and weight of the plot's frame, for the control strip's divider to match.
        /// </summary>
        public static readonly uint FrameColor = ColGraticule;
        public const float FrameWeight = GraticuleWeight;
        const float HoverFillAlpha = 0.65f;

        // NB: the plot of the lane the pointer works in, which every lane's matches in width.
        float plotLeft;
        float plotSpan;
        float visibleHeight;

        // NB: the plot column as last laid out, which the lanes are placed in before the column is entered.
        float plotColumnLeft;
        float plotColumnWidth;
        int inputLane;
        Lane[] placedLanes = Array.Empty<Lane>();

        readonly TraceRaster traces = new();

        /// <summary>
        /// Where the window's columns fall on the plot's pixels, from the plot as last laid out.
        /// </summary>
        PixelAxis Axis => new(window, plotLeft, plotSpan);

        // NB: read by nothing in the panel; the tests read it to see which rows are in view.
        RowLayout lastLayout;

        /// <summary>
        /// Lays out the label column and the plot. Each channel's row is centered on zero with +/- range / 2 at its
        /// edges, and a trace that exceeds its range runs into the neighboring channels' rows rather than being
        /// clipped at its own.
        /// </summary>
        /// <remarks>
        /// The graticules go in this window's draw list rather than the table's, so they stay put while the
        /// channels scroll, and lie under the traces. Lanes after the first have their labels in the plot column,
        /// beside their plots.
        /// </remarks>
        /// <param name="envelope">Per-column minima and maxima, one row per channel.</param>
        /// <param name="shown">The window <paramref name="envelope"/> holds, which the frame is drawn in.</param>
        void WaveformPlot(Envelope envelope, DisplayWindow shown)
        {
            var rows = envelope.Rows;
            var labelDigits = DigitCount(rowChannels is { Length: > 0 } map ? map.Max() : rows - 1);
            if (amplitudeView.Row != expandedRow || amplitudeView.Range != Range)
                amplitudeView.Row = -1;


            // NB: the axis labels get a button-height row, so the plot's frame starts one button-height down.
            var headerHeight = ImGui.GetFrameHeight();
            var labelY = ImGui.GetCursorScreenPos().Y + ImGui.GetStyle().FramePadding.Y;
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + headerHeight);

            var scrollHeight = ImGui.GetStyle().ScrollbarSize;
            var scrollGap = ImGui.GetStyle().ItemSpacing.Y / 2;
            var plotTop = ImGui.GetCursorScreenPos().Y;
            var tableHeight = ImGui.GetContentRegionAvail().Y - scrollHeight - scrollGap;
            var plotBottom = plotTop + tableHeight;
            var plotX = 0f;
            var plotWidth = 0f;
            var frames = Array.Empty<PlotFrame>();
            var frame = default(PlotFrame);

            // NB: ImGui scrolls one pixel at a time, but the panel often scrolls in increments of full rows, for the
            // zoom box, for collapsing a channel, and for W and S. Padding above the first row would offset those
            // scrolls and leave a sliver of the row above in view, so the ##rows window has no padding. ImGui's mouse
            // wheel scrolling is also turned off here, because it would scroll on Shift+wheel and Ctrl+wheel too.
            // HandleWheel handles every wheel gesture instead.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            var rowsOpen = ImGui.BeginChild("##rows", new Vector2(-1, tableHeight), ImGuiChildFlags.None,
                ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoNavInputs);
            ImGui.PopStyleVar();

            if (rowsOpen && ImGui.BeginTable("##table", 2, ImGuiTableFlags.NoSavedSettings))
            {
                var labelWidth = LabelColumnWidth(labelDigits);
                ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, labelWidth);
                ImGui.TableSetupColumn(string.Empty);
                ScrollIfPending();

                // NB: the table cells get no top padding either, for the same reason. The table reads the padding in
                // TableNextRow, so the push has to go around that call.
                ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(ImGui.GetStyle().CellPadding.X, 0));
                ImGui.TableNextRow();
                ImGui.PopStyleVar();
                ImGui.TableNextColumn();

                // NB: measured inside the cell, which leaves out the row's padding, so that rows fitted to it fill
                // the table without scrolling.
                visibleHeight = ImGui.GetContentRegionAvail().Y;
                var labelsLeft = ImGui.GetCursorScreenPos().X;
                var labelsRight = labelsLeft + ImGui.GetContentRegionAvail().X;
                var laneRows = LayoutRows(rows, plotTop, plotBottom, ImGui.GetCursorScreenPos().Y,
                    ImGui.GetContentRegionAvail().Y);
                var lanes = PlaceLanes(laneRows, labelsLeft, labelsRight - labelsLeft, plotColumnLeft, plotColumnWidth);
                placedLanes = lanes;
                var lane = InputLane(lanes);
                plotLeft = lanes[lane].Left;
                var layout = laneRows[lane];
                lastLayout = layout;
                // NB: only the row under the pointer takes input, so a click on whatever set the hovered channel is
                // not taken for a click on its row.
                var pointerRow = HoveredRow(lanes, lane);
                var elsewhere = hoveredElsewhere >= 0 ? RowOf(hoveredElsewhere) : -1;
                var hoveredRow = pointerRow >= 0 ? pointerRow : elsewhere < rows ? elsewhere : -1;
                lastHoveredRow = hoveredRow;
                if (selectedRow >= rows)
                    selectedRow = -1;
                if (selectOnShow && selectedRow < 0)
                    selectedRow = FirstShownInView(layout);
                selectOnShow = false;
                if (lanes[0].Strip)
                {
                    DrawLaneButton(ImGui.GetWindowDrawList(), 0, new Vector2(labelsLeft, plotTop), labelsRight - labelsLeft);
                    DrawFrame(ImGui.GetWindowDrawList(), labelsLeft, labelsRight - labelsLeft, plotTop, plotBottom);
                }
                else
                    ChannelLabels(laneRows[0], labelDigits, hoveredRow, labelsLeft, labelsRight);
                HandleChannelInput(pointerRow);
                AnswerTableKeys(layout);
                HandleWheel(layout);
                HandleDrags(layout);
                HandleZoomBox(layout);
                if (pendingReveal >= 0 && pendingReveal < rows)
                    ShowChannel(pendingReveal, layout);
                pendingReveal = -1;

                ImGui.TableNextColumn();
                plotX = ImGui.GetCursorScreenPos().X;
                plotWidth = ImGui.GetContentRegionAvail().X;
                plotColumnLeft = plotX;
                plotColumnWidth = plotWidth;
                lanes = PlaceLanes(laneRows, labelsLeft, labelsRight - labelsLeft, plotX, plotWidth);

                // NB: the first lane's strip button, pressed in the label column, can just have made the input lane
                // a strip.
                if (lanes[lane].Strip)
                    lane = inputLane = FirstFull(lanes);
                var laneWidth = lanes[FirstFull(lanes)].Width;
                var resized = laneWidth != plotSpan;
                plotLeft = lanes[lane].Left;
                plotSpan = laneWidth;

                // NB: the cells follow the plot's width, so a resized plot puts the window back on a cell boundary.
                if (resized && CanPan)
                    SetWindow(window);

                var full = Enumerable.Range(0, lanes.Length).Where(j => !lanes[j].Strip).ToArray();
                frames = full.Select(j => new PlotFrame(
                    envelope, lanes[j].Rows, new PixelAxis(shown, lanes[j].Left, plotSpan), plotTop, plotBottom,
                    sampleRate, Paused, CursorPosition, Timebase, pausedTimebase,
                    AmplitudeView.Span, AmplitudeView.Center, Unit, RowHidden, expandedRow, selectedRow, ShowHeatmap, ColorThreshold,
                    rowChannels)).ToArray();
                frame = frames[Array.IndexOf(full, lane)];

                // NB: an item the height of the tallest lane, which is what the table scrolls through. Everything in
                // the plot is drawn rather than laid out.
                ImGui.Dummy(new Vector2(plotWidth, laneRows.Max(rowsOf => rowsOf.Height)));
                var draw = ImGui.GetWindowDrawList();
                foreach (var j in full.Where(j => j > 0))
                    ChannelLabels(laneRows[j], labelDigits, hoveredRow, lanes[j].LabelLeft,
                        lanes[j].LabelLeft + labelsRight - labelsLeft);

                foreach (var f in frames)
                    DrawRowBand(draw, f);
                traces.Draw(frames, Colors.Packed(rowCount, rowChannels),
                    Paused || decimator is null ? -1 : decimator.Cursor, history?.Count ?? 0, FastSweep);

                foreach (var f in frames)
                {
                    // NB: a trace that runs off its row may run into its neighbors but not past the rows, which can
                    // end above the plot's bottom when there are few channels, or past its lane.
                    var hoveredHere = hoveredRow >= f.Layout.FirstRow && hoveredRow < f.Layout.LastRow;
                    draw.PushClipRect(new Vector2(f.Left, f.Layout.Origin),
                        new Vector2(f.Left + f.Width, f.Layout.Origin + f.Layout.Height), true);
                    if (expandedRow >= 0)
                        DrawChannelLines(draw, f, expandedRow, 1);
                    else if (hoveredHere && !RowHidden[hoveredRow] && !ShowHeatmap)
                        DrawChannelLines(draw, f, hoveredRow, HoverLineWeight);
                    DrawSweepCursor(draw, f);
                    draw.PopClipRect();

                    ShadeFrozenTail(draw, f.Axis, f.Left, f.Width, plotTop, plotBottom);

                    // NB: a heatmap row has no line to thicken and covers the band, so the hovered and selected rows
                    // are marked from outside, which also ties a label to its row when rows are far shorter than it.
                    var selectedHere = selectedRow >= f.Layout.FirstRow && selectedRow < f.Layout.LastRow;
                    if (ShowHeatmap && expandedRow < 0 && hoveredHere && !RowHidden[hoveredRow])
                        BracketRow(draw, f, hoveredRow, ColHoveredRow);
                    if (ShowHeatmap && expandedRow < 0 && selectedHere)
                        BracketRow(draw, f, selectedRow, WaveformCursors.Color);
                }

                // NB: ahead of the cursors so the don't compete for a click on [CLIPPING] in the plot area.
                var corner = frames[frames.Length - 1];
                var cornerTaken = DrawMaximize(draw, corner);
                for (int j = 0; j < lanes.Length && lanes.Length > 1; j++)
                {
                    var side = ImGui.GetFrameHeight();
                    var pad = ImGui.GetStyle().FramePadding;
                    var right = lanes[j].Left + lanes[j].Width - (j == full[full.Length - 1] ? cornerTaken : 0);
                    if (lanes[j].Strip && j > 0)
                        DrawLaneButton(draw, j, new Vector2(lanes[j].Left, plotTop), lanes[j].Width);
                    else if (!lanes[j].Strip)
                    {
                        DrawLaneButton(draw, j, new Vector2(right - pad.X - side, plotTop), side);
                        if (j == full[full.Length - 1])
                            cornerTaken += side + pad.X;
                    }
                }

                DrawClipping(draw, corner, cornerTaken);
                foreach (var f in frames)
                    cursors.Draw(draw, f, interactive: f.Left == frame.Left);
                DrawZoomBox(draw, frame);
                SelectClicked(frame, pointerRow);
                foreach (var placed in lanes)
                    DrawFrame(draw, placed.Left, placed.Width, plotTop, plotBottom);
                DrawReadout(draw, frame, pointerRow);
                AmplitudeLabels(draw, frame, plotX, plotTop, plotBottom);
                ImGui.EndTable();
            }
            ImGui.EndChild();

            if (plotWidth > 0)
            {
                foreach (var f in frames)
                {
                    DrawGraticules(ImGui.GetWindowDrawList(), f.Axis, f.Left, f.Width, plotTop, plotBottom, labelY);
                    cursors.DrawTimeLabels(ImGui.GetWindowDrawList(), f, labelY);
                }
                TimeScrollBar(plotX, plotWidth, plotBottom + scrollGap, scrollHeight);

                // NB: last, so that its window is drawn over the table's and takes the pointer from it.
                if (cursors.DrawTable(frames[0]) is long center)
                    CenterOn(center);
            }
        }

        /// <summary>
        /// One lane and where it falls on screen: its labels, then its plot, or a strip in their place while another
        /// lane is widened.
        /// </summary>
        internal readonly struct Lane
        {
            public readonly RowLayout Rows;
            public readonly float LabelLeft;
            public readonly float Left;
            public readonly float Width;
            public readonly bool Strip;

            public Lane(RowLayout rows, float labelLeft, float left, float width, bool strip)
            {
                Rows = rows;
                LabelLeft = labelLeft;
                Left = left;
                Width = width;
                Strip = strip;
            }
        }

        // NB: the first lane's labels, or its strip, take the label column, and the rest go in the plot column, each
        // lane's plot as wide as every other's. Every lane's labels are as far from its plot as the label column is
        // from the plot column, and that much less from the plot before.
        Lane[] PlaceLanes(RowLayout[] laneRows, float labelsLeft, float labelWidth, float columnLeft, float columnWidth)
        {
            var count = laneRows.Length;
            if (count == 1)
                return new[] { new Lane(laneRows[0], labelsLeft, columnLeft, columnWidth, false) };

            var labelGap = Math.Max(0, columnLeft - labelsLeft - labelWidth);
            var gap = Math.Max(2, ImGui.GetStyle().ItemSpacing.X - labelGap);
            var stripWidth = ImGui.GetFrameHeight();
            bool IsStrip(int lane) => expandedLane >= 0 && lane != expandedLane;
            var fullCount = expandedLane >= 0 ? 1 : count;
            var laterLabels = fullCount - (IsStrip(0) ? 0 : 1);
            var laterStrips = count - 1 - laterLabels;
            var inColumn = count - (IsStrip(0) ? 1 : 0);
            var width = Math.Max(1, MathF.Floor(
                (columnWidth - laterLabels * (labelWidth + labelGap) - laterStrips * stripWidth - (inColumn - 1) * gap) /
                fullCount));

            var lanes = new Lane[count];
            var x = columnLeft;
            for (int j = 0; j < count; j++)
            {
                if (j == 0 && IsStrip(0))
                    lanes[0] = new Lane(laneRows[0], labelsLeft, labelsLeft, labelWidth, true);
                else if (j == 0)
                {
                    lanes[0] = new Lane(laneRows[0], labelsLeft, x, width, false);
                    x += width + gap;
                }
                else if (IsStrip(j))
                {
                    lanes[j] = new Lane(laneRows[j], x, x, stripWidth, true);
                    x += stripWidth + gap;
                }
                else
                {
                    lanes[j] = new Lane(laneRows[j], x, x + labelWidth + labelGap, width, false);
                    x += labelWidth + labelGap + width + gap;
                }
            }

            return lanes;
        }

        static int FirstFull(Lane[] lanes)
        {
            for (int j = 0; j < lanes.Length; j++)
            {
                if (!lanes[j].Strip)
                    return j;
            }
            return 0;
        }

        // NB: the lane under the pointer, kept while a button is held so a drag stays in the lane it began in.
        int InputLane(Lane[] lanes)
        {
            if (!ImGui.IsAnyMouseDown())
            {
                var x = ImGui.GetMousePos().X;
                for (int j = lanes.Length - 1; j >= 0; j--)
                {
                    if (!lanes[j].Strip && x >= lanes[j].LabelLeft)
                    {
                        inputLane = j;
                        break;
                    }
                }
            }

            if (inputLane >= lanes.Length || lanes[inputLane].Strip)
                inputLane = FirstFull(lanes);
            return inputLane;
        }

        /// <summary>
        /// Draws the button that widens lane <paramref name="lane"/>, shrinking the others to strips, or that gives
        /// the lanes equal widths again while it is the one widened.
        /// </summary>
        /// <remarks>
        /// Faint until hovered, as the maximize button is.
        /// </remarks>
        void DrawLaneButton(ImDrawListPtr draw, int lane, Vector2 corner, float width)
        {
            var side = Math.Min(width, ImGui.GetFrameHeight());
            var min = new Vector2(corner.X + (width - side) / 2, corner.Y + ImGui.GetStyle().FramePadding.Y);
            var max = min + new Vector2(side);

            ImGui.SetCursorScreenPos(min);
            if (ImGui.InvisibleButton($"##lane{lane}", new Vector2(side)))
                ToggleLane(lane);
            var hovered = ImGui.IsItemHovered();
            var widened = expandedLane == lane;
            ImGuiWidgets.Tooltip((widened ? "Give the lanes equal widths again" : "Widen this lane, shrinking the others") +
                "\nKey: L over the lane");

            if (hovered)
                draw.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.ButtonHovered), ImGui.GetStyle().ChildRounding);

            // NB: arrows out from the middle to widen, and in toward it to give the width back.
            var color = ImGui.GetColorU32(ImGuiCol.Text, hovered ? 1f : 0.25f);
            var middle = (min + max) / 2;
            var reach = MathF.Floor(side * 0.3f);
            var head = MathF.Floor(side * 0.15f);
            draw.AddLine(middle - new Vector2(reach, 0), middle + new Vector2(reach, 0), color);
            foreach (var direction in new[] { -1f, 1f })
            {
                var tip = widened ? middle + new Vector2(direction * head, 0) : middle + new Vector2(direction * reach, 0);
                var back = widened ? head : -head;
                draw.AddLine(tip, tip + new Vector2(direction * back, -head), color);
                draw.AddLine(tip, tip + new Vector2(direction * back, head), color);
            }
        }

        void ToggleLane(int lane) => expandedLane = expandedLane == lane ? -1 : lane;

        // NB: what the button of the lane under the pointer does, or else the selected channel's lane's, as when the
        // pointer is over the control strip. A gap between lanes belongs to the lane on its left.
        void LaneKey(RowLayout layout)
        {
            var lanes = placedLanes;
            if (lanes.Length < 2)
                return;

            var mouse = ImGui.GetMousePos();
            var last = lanes[lanes.Length - 1];
            var lane = -1;
            if (mouse.Y >= layout.Top && mouse.Y < layout.Bottom &&
                mouse.X >= lanes[0].LabelLeft && mouse.X < last.Left + last.Width)
            {
                lane = lanes.Length - 1;
                while (mouse.X < lanes[lane].LabelLeft)
                    lane--;
            }
            else if (selectedRow >= 0)
                lane = LaneOf(selectedRow);

            if (lane >= 0)
                ToggleLane(lane);
        }

        /// <summary>
        /// Veils the part of a paused plot that is still showing the tail of the previous sweep.
        /// </summary>
        /// <remarks>
        /// Everything right of the cursor is older than everything left of it, and appears again elsewhere in
        /// the history. The shading marks it as stale.
        /// </remarks>
        void ShadeFrozenTail(ImDrawListPtr draw, in PixelAxis axis, float left, float width, float top, float bottom)
        {
            if (!Paused)
                return;

            var shown = axis.Window;
            var split = Math.Max(shown.Start, Math.Min(shown.End, CursorPosition));
            if (split >= shown.End)
                return;

            var x = axis.X(split);
            draw.AddRectFilled(
                new Vector2(x, MathF.Floor(top) + GraticuleWeight),
                new Vector2(MathF.Floor(left + width) - GraticuleWeight, MathF.Floor(bottom) - GraticuleWeight),
                ColFrozenTail);
        }

        /// <summary>
        /// The time the axis labels give <paramref name="position"/>: live, from the start of the sweep; paused,
        /// before the moment of pausing, with the frozen tail a whole paused timebase older.
        /// </summary>
        internal static double SecondsAt(in PlotFrame frame, double position)
        {
            var origin = frame.Paused ? frame.PauseOrigin : 0;
            var seconds = (position - origin) / frame.Window.Span * frame.Timebase;
            return frame.Paused && position > origin ? seconds - frame.PausedTimebase : seconds;
        }

        /// <summary>
        /// Shows the channel, time and amplitude under the pointer in the plot's lower right corner.
        /// </summary>
        /// <remarks>
        /// The time reads as the time axis labels do, and the amplitude from the center of the channel's row.
        /// </remarks>
        void DrawReadout(ImDrawListPtr draw, in PlotFrame frame, int hoveredRow)
        {
            var mouse = ImGui.GetMousePos();
            if (hoveredRow < 0 || mouse.X < frame.Left || mouse.X >= frame.Left + frame.Width)
                return;

            var seconds = SecondsAt(frame, frame.Axis.PositionAt(mouse.X));
            var amplitude = frame.ValueAt(hoveredRow, mouse.Y);

            // NB: an expanded channel is named in the plot's top left already.
            var position = $"{seconds:0.0000} s   {amplitude:0.0} {frame.Unit}";
            var text = frame.Expanded >= 0 ? position : $"Ch {ChannelOf(hoveredRow)}   {position}";
            var corner = new Vector2(frame.Left + frame.Width, frame.Bottom) - ImGui.CalcTextSize(text) -
                2 * ImGui.GetStyle().FramePadding;
            PlotText.Framed(draw, corner, text);
        }

        /// <summary>
        /// A horizontal bar below the plot showing where the window sits on the axis, which can be dragged
        /// to move it.
        /// </summary>
        /// <remarks>
        /// Drawn rather than taken from ImGui, which scrolls its own containers and has no notion of this
        /// axis. It is styled from the scrollbar colors so that it reads as one alongside the channel
        /// scrollbar it sits under.
        /// </remarks>
        void TimeScrollBar(float left, float width, float top, float height)
        {
            var axisFirst = Paused && history is not null ? AxisFirst(history) : window.Start;
            var axisLast = Paused && history is not null ? AxisLast : window.End;
            var travel = Math.Max(0, axisLast - axisFirst - window.Span);

            // NB: the thumb sits inside the track by the same margin ImGui gives its own grabs, which is
            // most of what makes a scrollbar read as one rather than as a filled bar.
            var inset = Math.Max(0f, Math.Min(3f, MathF.Floor((height - 2f) / 2f)));
            var trackLeft = left + inset;
            var trackWidth = width - 2 * inset;

            // NB: the thumb keeps a minimum width so that a window which is a thousandth of the axis is
            // still something to aim at, which costs a little of the travel it stands for.
            var reach = (double)(travel + window.Span);
            var thumbWidth = Math.Max(ImGui.GetStyle().GrabMinSize, (float)(trackWidth * window.Span / reach));
            var slack = trackWidth - thumbWidth;
            var thumbX = travel > 0
                ? trackLeft + slack * (float)((window.Start - axisFirst) / (double)travel)
                : trackLeft;

            ImGui.SetCursorScreenPos(new Vector2(left, top));
            ImGui.InvisibleButton("##timescroll", new Vector2(width, height));
            ImGuiWidgets.Tooltip("Drag to move back and forth through the recent signal while paused.\n" +
                "Keys: A and D\nOn the plot: Shift+Wheel, or drag with the middle button");

            if (ImGui.IsItemActive() && travel > 0 && slack > 0)
                Pan(-(long)(ImGui.GetIO().MouseDelta.X * (travel / slack)));

            var rounding = ImGui.GetStyle().ScrollbarRounding;
            var grab = ImGui.IsItemActive() ? ImGuiCol.ScrollbarGrabActive
                : ImGui.IsItemHovered() ? ImGuiCol.ScrollbarGrabHovered
                : ImGuiCol.ScrollbarGrab;

            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(new Vector2(left, top), new Vector2(left + width, top + height),
                ImGui.GetColorU32(ImGuiCol.ScrollbarBg), rounding);
            draw.AddRectFilled(
                new Vector2(thumbX, top + inset), new Vector2(thumbX + thumbWidth, top + height - inset),
                ImGui.GetColorU32(grab), rounding);
        }

        /// <summary>
        /// The rows the plot spans this frame and where they fall on screen: every channel at
        /// <see cref="ChannelHeight"/>, or the expanded channel alone filling the visible height.
        /// </summary>
        /// <remarks>
        /// Each row is one row height tall, at its slot: its offset from the top in row heights, k for row k
        /// or where the owner's positions put it, which can leave slots empty or stack rows. A lane's layout spans
        /// its own rows, and its slots count from the top of every lane, so lanes line up.
        /// </remarks>
        internal readonly struct RowLayout
        {
            public readonly int FirstRow;
            public readonly int LastRow;
            public readonly float RowHeight;
            public readonly float Top;
            public readonly float Bottom;
            public readonly float Origin;
            public readonly float[]? Slots;

            public RowLayout(int firstRow, int lastRow, float rowHeight, float top, float bottom, float origin,
                float[]? slots = null)
            {
                FirstRow = firstRow;
                LastRow = lastRow;
                RowHeight = rowHeight;
                Top = top;
                Bottom = bottom;
                Origin = origin;
                Slots = slots;
            }

            // NB: rows past either end continue one row height apart, so the top of the row after the last is the
            // rows' bottom edge.
            public static float SlotOf(float[]? slots, int row) =>
                slots is { Length: > 0 }
                    ? row < 0 ? slots[0] + row
                    : row >= slots.Length ? slots[slots.Length - 1] + row - (slots.Length - 1)
                    : slots[row]
                    : row;

            public float Slot(int row) => SlotOf(Slots, row);

            // NB: the slot at the origin: the first row's when evenly spaced, or else the top of every lane.
            public float Base => Slots is null ? FirstRow : 0;

            /// <summary>
            /// The slot at screen height <paramref name="y"/>, with its fractional part.
            /// </summary>
            public float SlotAt(float y) => Base + (y - Origin) / RowHeight;

            public float Height => LastRow > FirstRow ? (Slot(LastRow - 1) + 1 - Base) * RowHeight : 0;

            public int FirstVisible => Slots is null
                ? Math.Max(FirstRow, RowAt(Top))
                : FirstRowFrom(SlotAt(Top) - 1, inclusive: false);

            public int LastVisible => Slots is null
                ? Math.Min(LastRow, FirstRow + (int)Math.Ceiling((Bottom - Origin) / RowHeight))
                : FirstRowFrom(SlotAt(Bottom), inclusive: true);

            public float RowTop(int row) => Origin + (Slot(row) - Base) * RowHeight;

            /// <summary>
            /// The row at screen height <paramref name="y"/>: the one drawn last if rows share a slot, -1 in a gap,
            /// and before the first or after the last outside the rows.
            /// </summary>
            public int RowAt(float y)
            {
                if (Slots is null)
                    return FirstRow + (int)Math.Floor((y - Origin) / RowHeight);

                var slot = SlotAt(y);
                var first = Slot(FirstRow);
                if (slot < first)
                    return FirstRow + (int)Math.Floor(slot - first);

                var row = FirstRowFrom(slot, inclusive: false) - 1;
                var end = Slot(row) + 1;
                return slot < end ? row
                    : row == LastRow - 1 ? LastRow + (int)Math.Floor(slot - end)
                    : -1;
            }

            // NB: the first row whose slot is after the given one (or at it, if inclusive), else LastRow. A binary
            // search, since slots never decrease.
            int FirstRowFrom(float slot, bool inclusive)
            {
                int lo = FirstRow, hi = LastRow;
                while (lo < hi)
                {
                    var mid = (lo + hi) / 2;
                    var at = Slot(mid);
                    if (inclusive ? at >= slot : at > slot)
                        hi = mid;
                    else
                        lo = mid + 1;
                }
                return lo;
            }
        }

        // NB: an expanded channel fills the plot alone, so its layout is one row, wherever its slot or lane.
        RowLayout[] LayoutRows(int rows, float top, float bottom, float origin, float available)
        {
            if (expandedRow >= rows)
                Collapse();

            if (expandedRow >= 0)
                return new[] { new RowLayout(expandedRow, expandedRow + 1, available, top, bottom, origin) };

            var slots = SlotsFor(rows);
            var starts = LanesFor(rows) ?? new[] { 0, rows };
            var layouts = new RowLayout[starts.Length - 1];
            for (int lane = 0; lane < layouts.Length; lane++)
                layouts[lane] = new RowLayout(starts[lane], starts[lane + 1], ChannelHeight, top, bottom, origin, slots);

            if (expandedLane >= layouts.Length)
                expandedLane = -1;
            return layouts;
        }

        static float LabelColumnWidth(int labelDigits) => labelDigits * ImGui.CalcTextSize("0").X;

        static readonly int[] LabelSteps = { 1, 2, 5 };

        // NB: the half pixel of slack is as much as rounding can leave evenly spaced labels short.
        static int GridStep(float rowHeight, float lineHeight)
        {
            for (var decade = 1; decade < 1_000_000; decade *= 10)
            {
                foreach (var factor in LabelSteps)
                {
                    if (factor * decade * rowHeight >= lineHeight - 0.5f)
                        return factor * decade;
                }
            }
            return 1_000_000;
        }

        unsafe void ChannelLabels(in RowLayout layout, int labelDigits, int hoveredRow, float left, float right)
        {
            var labelBuffer = stackalloc byte[32];
            var label = new StrBuilder(labelBuffer, 32);
            var textOffset = (layout.RowHeight - ImGui.GetTextLineHeight()) / 2;
            var draw = ImGui.GetWindowDrawList();

            // NB: rows shorter than the text are labeled on a grid of 1, 2, 5, 10, 20, ... slots, the finest whose
            // cells are a line tall, each cell labeling its first row. The grid is fixed to the slots, so labels
            // keep their places as the rows scroll, and fall at even depths where rows lie unevenly. The hovered
            // channel and the one the cursors read are always labeled, and hide only the grid labels they overlap.
            var lineHeight = ImGui.GetTextLineHeight();
            var step = GridStep(layout.RowHeight, lineHeight);

            var selected = selectedRow;
            var rows = layout;
            // NB: only by rows of this lane, since another lane's labels are beside these rather than over them.
            bool Crowds(int row, int priority) =>
                priority >= rows.FirstRow && priority < rows.LastRow && priority != row &&
                Math.Abs(rows.RowTop(row) - rows.RowTop(priority)) < lineHeight;

            // NB: the slack keeps a slot computed as 2.9999 in the cell of 3.
            int Cell(int row) => (int)MathF.Floor(rows.Slot(row) / step + 0.001f);

            // NB: a cell's first row can lie near its end, and the next cell's near its start; the second is skipped.
            var nextFree = float.NegativeInfinity;

            // NB: from the first row whose label reaches into view, so that the grid labels in view are spaced as
            // they would be scrolled further down.
            var first = layout.FirstVisible;
            while (first > layout.FirstRow && layout.RowTop(first - 1) + lineHeight > layout.Top)
                first--;

            // NB: an expanded channel is named inside the plot instead, by AmplitudeLabels.
            for (int i = first; i < layout.LastVisible && expandedRow < 0; i++)
            {
                var gridded = (i == layout.FirstRow || Cell(i) != Cell(i - 1)) && layout.RowTop(i) >= nextFree;
                if (gridded)
                    nextFree = layout.RowTop(i) + lineHeight - 0.5f;

                var name = ChannelOf(i);
                label.Reset();
                for (var n = DigitCount(name); n < labelDigits; n++)
                    label.Append('0');
                label.Append(name);
                label.End();

                // NB: at least as tall as the label, centered on the row as the label is, so that on a row shorter
                // than the text the highlight frames it rather than striking it through.
                var markHeight = Math.Max(layout.RowHeight, lineHeight);
                var markTop = layout.RowTop(i) + (layout.RowHeight - markHeight) / 2;

                // NB: a clipping channel's label flashes, in step with the plot's CLIPPING, and is always written.
                var clipping = Clipping(i);
                if (clipping && ClipFlashOn)
                    draw.AddRectFilled(new Vector2(left, markTop), new Vector2(right, markTop + markHeight), ColClipping);

                // Highlight hovered channel and the selected one
                if ((i == hoveredRow || i == selected) && expandedRow < 0)
                {
                    var fill = Colors.Of(i, name);
                    fill.W = HoverFillAlpha;
                    draw.AddRectFilled(new Vector2(left, markTop), new Vector2(right, markTop + markHeight),
                        ImGui.ColorConvertFloat4ToU32(fill));
                }

                if (i == selected && expandedRow < 0)
                {
                    var boxTop = MathF.Floor(markTop);
                    draw.AddRect(new Vector2(left, boxTop), new Vector2(right, boxTop + markHeight), WaveformCursors.Color);
                }

                var priority = i == hoveredRow || i == selected || clipping;
                if (!priority && (!gridded || Crowds(i, hoveredRow) || Crowds(i, selected)))
                    continue;

                // NB: drawn rather than laid out, since a label on a row shorter than the text reaches past it,
                // and on the last rows would stretch what the rows scroll through and bring up a scroll bar.
                var color = ImGui.GetColorU32(RowHidden[i] ? ImGuiCol.TextDisabled : ImGuiCol.Text);
                draw.AddText(new Vector2(left, layout.RowTop(i) + textOffset), color, label);
            }
        }

        static int DigitCount(int value)
        {
            var digits = 1;
            for (; value >= 10; value /= 10)
                digits++;
            return digits;
        }

        /// <summary>
        /// Outlines a channel's trace, its max and min, or the line through its samples where each column holds
        /// one. Drawn every frame over the traces' texture, for the one channel hovered or expanded.
        /// </summary>
        unsafe void DrawChannelLines(ImDrawListPtr draw, in PlotFrame frame, int row, float weight)
        {
            frame.Envelope.Min.GetRawData(out IntPtr minPtr, out int minStep, out Size shape);
            frame.Envelope.Max.GetRawData(out IntPtr maxPtr, out int maxStep, out Size _);
            var minLine = (float*)((byte*)minPtr + row * minStep);
            var maxLine = (float*)((byte*)maxPtr + row * maxStep);
            var columns = shape.Width;
            var axis = frame.Axis;

            // NB: a point per pixel where pixels combine columns, read as the texture beneath reads them, or else
            // one per column, at its left edge.
            var points = axis.Combines ? axis.Width : columns;
            var low = stackalloc Vector2[points];
            var high = stackalloc Vector2[points];
            for (int i = 0; i < points; i++)
            {
                var (min, max) = axis.Combines ? TraceRaster.Bin(minLine, maxLine, axis, i) : (minLine[i], maxLine[i]);
                var x = axis.Combines ? axis.Left + i : axis.ColumnX(i);
                low[i] = new Vector2(x, frame.ValueY(row, min));
                high[i] = new Vector2(x, frame.ValueY(row, max));
            }

            var color = ImGui.ColorConvertFloat4ToU32(Colors.Of(row, ChannelOf(row)));
            Segments(draw, low, points, color, weight);
            if (frame.Window.Step > 1 || axis.Combines && points < columns)
                Segments(draw, high, points, color, weight);
        }

        // NB: one segment at a time rather than a polyline, whose mitred joins overshoot a sharp turn by many
        // pixels on a noisy trace. A segment touching a column that holds nothing is left out, so empty columns
        // are a gap. AddLine moves its ends half a pixel, which is taken back.
        static unsafe void Segments(ImDrawListPtr draw, Vector2* points, int count, uint color, float weight)
        {
            var half = new Vector2(0.5f);
            for (int i = 1; i < count; i++)
            {
                if (!float.IsNaN(points[i - 1].Y) && !float.IsNaN(points[i].Y))
                    draw.AddLine(points[i - 1] - half, points[i] - half, color, weight);
            }
        }

        /// <summary>
        /// Marks the column being written, or where the display was paused.
        /// </summary>
        void DrawSweepCursor(ImDrawListPtr draw, in PlotFrame frame)
        {
            if (FastSweep || decimator is null)
                return;

            // NB: paused, the pause instant, which can fall partway through a column.
            var shown = frame.Window;
            var position = Paused ? CursorPosition : shown.PositionOf(decimator.Cursor);
            if (position < shown.Start || position >= shown.End)
                return;

            // NB: on the pixel the position is drawn in. AddLine moves its ends half a pixel, which centers the line
            // on that pixel, and is taken back vertically.
            var x = frame.Axis.X(position);
            var top = frame.Layout.Origin - 0.5f;
            draw.AddLine(new Vector2(x, top), new Vector2(x, top + frame.Layout.Height), ColSweepCursor, SweepCursorWeight);
        }

        /// <summary>
        /// Shades the row of the selected channel.
        /// </summary>
        /// <remarks>
        /// Drawn ahead of the traces, so the selection is marked without anything drawn over the trace. Not in the
        /// heatmap, which covers it, and where lines above and below the row mark it instead.
        /// </remarks>
        static void DrawRowBand(ImDrawListPtr draw, in PlotFrame frame)
        {
            var layout = frame.Layout;
            if (frame.Selected < layout.FirstRow || frame.Selected >= layout.LastRow || frame.Expanded >= 0 ||
                frame.Heatmap)
                return;

            var top = MathF.Floor(frame.Layout.RowTop(frame.Selected));
            var bottom = MathF.Floor(frame.Layout.RowTop(frame.Selected) + frame.Layout.RowHeight);
            draw.AddRectFilled(new Vector2(frame.Left, top), new Vector2(frame.Left + frame.Width, bottom),
                ColSelectedRow);
        }

        /// <summary>
        /// Draws the box a right-drag is zooming to, while it is held.
        /// </summary>
        /// <remarks>
        /// An axis the box is too short along to zoom is drawn across the whole plot, since the zoom leaves it as it
        /// is, so the box shows what letting go will do.
        /// </remarks>
        void DrawZoomBox(ImDrawListPtr draw, in PlotFrame frame)
        {
            var plotMin = PlotMin(frame.Top);
            var plotMax = PlotMax(frame.Bottom);
            if (zoomBox.Current(plotMin, plotMax) is not { } box || !box.Wide && !box.Tall)
                return;

            var min = new Vector2(box.Wide ? box.Min.X : plotMin.X, box.Tall ? box.Min.Y : plotMin.Y);
            var max = new Vector2(box.Wide ? box.Max.X : plotMax.X, box.Tall ? box.Max.Y : plotMax.Y);
            PixelRect.Fill(draw, min, max, ColZoomBoxFill);
            PixelRect.Outline(draw, min, max, ColZoomBox);
        }

        // NB: after the cursors, so that a click that grabs a cursor's line does not also select.
        void SelectClicked(in PlotFrame frame, int hoveredRow)
        {
            var pointer = ImGui.GetMousePos();
            var inPlot = ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() &&
                pointer.X >= frame.Left && pointer.X < frame.Left + frame.Width &&
                pointer.Y >= frame.Top && pointer.Y < frame.Bottom;
            if (inPlot && !cursors.Dragging && hoveredRow >= 0 && !RowHidden[hoveredRow] && expandedRow < 0 &&
                ImGui.IsMouseClicked(ImGuiMouseButton.Left) && Modifiers())
            {
                selectedRow = hoveredRow;
            }
        }

        /// <summary>
        /// Draws the button in the plot's top right corner that gives the plot the whole window or gives the rest
        /// back, and returns the width it takes from the corner.
        /// </summary>
        /// <remarks>
        /// Faint until hovered, since it sits over the traces.
        /// </remarks>
        float DrawMaximize(ImDrawListPtr draw, in PlotFrame frame)
        {
            var pad = ImGui.GetStyle().FramePadding;
            var side = ImGui.GetFrameHeight();
            var min = new Vector2(frame.Left + frame.Width - pad.X - side, frame.Top + pad.Y);
            var max = min + new Vector2(side);

            ImGui.SetCursorScreenPos(min);
            if (ImGui.InvisibleButton("##maximize", new Vector2(side)))
                Maximized = !Maximized;
            var hovered = ImGui.IsItemHovered();
            ImGuiWidgets.Tooltip(Maximized ? "Restore the layout\nKey: M" : "Give the plot the whole window\nKey: M");

            if (hovered)
                draw.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.ButtonHovered), ImGui.GetStyle().ChildRounding);

            // NB: a window's glyph, one square to maximize, and to restore one square in front of another, of which
            // only the top and right edges show.
            var color = ImGui.GetColorU32(ImGuiCol.Text, hovered ? 1f : 0.25f);
            var inset = MathF.Floor(side * 0.28f);
            var a = min + new Vector2(inset);
            var b = max - new Vector2(inset);
            if (Maximized)
            {
                var offset = MathF.Floor(side * 0.12f);
                var front = (Min: a + new Vector2(0, offset), Max: b + new Vector2(-offset, 0));
                var back = (Min: front.Min + new Vector2(offset, -offset), Max: front.Max + new Vector2(offset, -offset));
                draw.AddRect(front.Min, front.Max, color);
                draw.AddLine(back.Min, new Vector2(back.Max.X, back.Min.Y), color);
                draw.AddLine(new Vector2(back.Max.X, back.Min.Y), back.Max, color);
            }
            else
            {
                draw.AddRect(a, b, color);
            }

            return side + pad.X;
        }

        /// <summary>
        /// Flashes CLIPPING in the plot's top right corner while any channel is clipping, as a scope's overload
        /// light does, as a button that brings the first clipping channel into view.
        /// </summary>
        void DrawClipping(ImDrawListPtr draw, in PlotFrame frame, float right)
        {
            var first = -1;
            for (int c = 0; c < lastClip.Length && first < 0; c++)
            {
                if (Clipping(c))
                    first = c;
            }

            if (first < 0)
                return;

            const string Text = "CLIPPING";
            var pad = ImGui.GetStyle().FramePadding;
            var size = ImGui.CalcTextSize(Text);
            var corner = new Vector2(frame.Left + frame.Width - right - size.X - 2 * pad.X, frame.Top + 2 * pad.Y);

            // NB: only the text flashes, so that a click is never lost to the dark half of a flash.
            ImGui.SetCursorScreenPos(corner - pad);
            if (ImGui.InvisibleButton("##clipping", size + 2 * pad))
                ShowChannel(first, frame.Layout);
            ImGuiWidgets.Tooltip("Some channels are clipping: their signal reaches the limit of the amplifier's range. " +
                "Click to bring the first one into view.");

            var rounding = ImGui.GetStyle().ChildRounding;
            var fill = ImGui.IsItemHovered() ? ImGui.GetColorU32(ImGuiCol.ButtonHovered) : PlotText.LabelBackground;
            draw.AddRectFilled(corner - pad, corner + size + pad, fill, rounding);
            draw.AddRect(corner - pad, corner + size + pad, ColClipping, rounding);
            if (ClipFlashOn)
                draw.AddText(corner, ColClipping, Text);
        }

        // NB: If not expanded, center the channel. If expanded, show that channel in expanded view.
        void ShowChannel(int row, in RowLayout layout)
        {
            if (expandedRow >= 0)
            {
                expandedRow = row;
                return;
            }

            // NB: rows lie level across lanes, so the scroll below holds whichever lane the row is in.
            if (expandedLane >= 0)
                expandedLane = LaneOf(row);

            var center = layout.RowTop(row) + layout.RowHeight / 2;
            ImGui.SetScrollY(ImGui.GetScrollY() + center - (layout.Top + layout.Bottom) / 2);
        }

        /// <summary>
        /// Marks a channel's row with a line just above it and another just below, leaving the row as drawn.
        /// </summary>
        internal static void BracketRow(ImDrawListPtr draw, in PlotFrame frame, int row, uint color)
        {
            var top = MathF.Floor(frame.Layout.RowTop(row));
            var bottom = MathF.Floor(frame.Layout.RowTop(row) + frame.Layout.RowHeight);
            var right = frame.Left + frame.Width;
            draw.AddRectFilled(new Vector2(frame.Left, top - 1), new Vector2(right, top), color);
            draw.AddRectFilled(new Vector2(frame.Left, bottom), new Vector2(right, bottom + 1), color);
        }

        // NB: filled rects on whole pixels, as PixelRect draws, here and in DrawGraticules and BracketRow.
        static void DrawFrame(ImDrawListPtr draw, float left, float width, float top, float bottom) =>
            PixelRect.Outline(draw, new Vector2(left, top), new Vector2(left + width, bottom), ColGraticule,
                GraticuleWeight);

        /// <summary>
        /// Draws the time divisions and their labels.
        /// </summary>
        /// <remarks>
        /// Live, zero is the start of the sweep. Paused, zero is the cursor and every label is how long before
        /// the pause that sample was taken, so the frozen tail right of the cursor reads oldest. Divisions are
        /// fixed to positions on the axis rather than to the plot, so they pan with the data.
        /// </remarks>
        void DrawGraticules(
            ImDrawListPtr draw, in PixelAxis axis, float left, float width, float top, float bottom, float labelY)
        {
            var shown = axis.Window;
            var t = MathF.Floor(top) + GraticuleWeight;
            var b = MathF.Floor(bottom) - GraticuleWeight;
            var textColor = ImGui.GetColorU32(ImGuiCol.Text);

            // NB: a tenth of the window's span, not of the timebase. The span is a whole number of columns
            // and slightly shorter, so a tenth of the timebase would not fit ten times and the last
            // division would flicker. Labels still show the round timebase.
            var interval = shown.Span / (double)TimeDivisions;
            var origin = Paused ? CursorPosition : 0;

            for (var d = (long)Math.Ceiling((shown.Start - origin) / interval);
                 d <= (long)Math.Floor((shown.End - origin) / interval);
                 d++)
            {
                var position = origin + d * interval;
                var x = axis.X(position);

                // NB: skip divisions on the edges, where the frame already draws a line.
                var l = MathF.Floor(x);
                if (l > left + 1 && l < left + width - 1)
                    DivisionDots(draw, new Vector2(l, t), b - t, vertical: true);

                // NB: one spacing across the whole plot, so labels on either side of the cursor never
                // overlap. Right of the cursor is the frozen tail, a whole frozen window older.
                var seconds = d * Timebase / TimeDivisions;
                if (Paused && position > CursorPosition)
                    seconds -= pausedTimebase;

                // NB: kept within the plot, since one at an edge would be clipped by the window or run into the
                // next lane's.
                var label = $"{seconds:g}";
                var labelWidth = ImGui.CalcTextSize(label).X;
                var labelX = Math.Max(left, Math.Min(left + width - labelWidth, x - labelWidth / 2));
                draw.AddText(new Vector2(labelX, labelY), textColor, label);
            }

            if (expandedRow < 0)
                return;

            // NB: an expanded channel fills the plot, so its row is the plot, and a tenth of the range is a
            // tenth of the height. The outermost divisions lie on the frame.
            var l0 = MathF.Floor(left) + GraticuleWeight;
            for (int k = 1; k < AmplitudeDivisions; k++)
                DivisionDots(draw, new Vector2(l0, MathF.Floor(AmplitudeDivisionY(k, top, bottom))),
                    MathF.Floor(left + width) - GraticuleWeight - l0, vertical: false);
        }

        /// <summary>
        /// Height on screen of amplitude division <paramref name="k"/> of an expanded channel, counted down from
        /// the top of the plot.
        /// </summary>
        static float AmplitudeDivisionY(int k, float top, float bottom) =>
            top + (bottom - top) * k / AmplitudeDivisions;

        /// <summary>
        /// Labels an expanded channel's amplitude divisions down the left of the plot, and names the channel
        /// and both scales in its top left corner.
        /// </summary>
        /// <remarks>
        /// Drawn over the traces, where the divisions themselves are drawn under them.
        /// </remarks>
        void AmplitudeLabels(ImDrawListPtr draw, in PlotFrame frame, float left, float top, float bottom)
        {
            if (expandedRow < 0)
                return;

            var pad = ImGui.GetStyle().FramePadding;
            var color = ImGui.GetColorU32(ImGuiCol.TextDisabled);
            for (int k = 1; k < AmplitudeDivisions; k++)
            {
                var text = $"{PlotText.Significant(frame.Center + frame.Range * (AmplitudeDivisions / 2 - k) / AmplitudeDivisions)}";
                var y = AmplitudeDivisionY(k, top, bottom) - ImGui.GetTextLineHeight() / 2;
                draw.AddText(new Vector2(left + pad.X, y), color, text);
            }

            var timeDivision = Timebase / TimeDivisions;
            var time = timeDivision < 1 ? $"{PlotText.Significant(timeDivision * 1000)} ms/div" : $"{PlotText.Significant(timeDivision)} s/div";
            var unit = string.IsNullOrEmpty(Unit) ? "a.u." : Unit;
            var name = $"Ch {ChannelOf(expandedRow)}   {PlotText.Significant(frame.Range / AmplitudeDivisions)} {unit}/div   {time}";
            PlotText.Framed(draw, new Vector2(left, top) + 2 * pad, name);
        }

        static void DivisionDots(ImDrawListPtr draw, Vector2 start, float length, bool vertical)
        {
            // NB: whole-pixel rects, as for the frame, since a 1 px line drawn any other way blurs.
            for (var d = 0f; d < length; d += GraticuleWeight + DivisionDotGap)
            {
                var at = vertical ? start + new Vector2(0, d) : start + new Vector2(d, 0);
                draw.AddRectFilled(at, at + new Vector2(GraticuleWeight), ColDivision);
            }
        }
    }
}
