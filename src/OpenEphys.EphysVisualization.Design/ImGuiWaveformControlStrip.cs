using System;
using System.Collections.Generic;
using System.Numerics;
using Hexa.NET.ImGui;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// The row of controls under a waveform viewer's plot: whichever columns its owner gives it, laid out on a
    /// wrapping grid, with pause and the record length beside them.
    /// </summary>
    /// <remarks>
    /// One way to reach the panel's settings rather than a part of it: the panel plots and interprets gestures
    /// whether or not a strip is drawn, and reads nothing back from here. Widgets take the current value from the
    /// panel every frame, so a setting changed by gesture shows up here without anything being told about it.
    /// </remarks>
    internal sealed class ImGuiWaveformControlStrip
    {
        /// <summary>
        /// Width every control is given. Wide enough for the longest palette name, which is the widest
        /// preview text any of these show, and for every label, so one width serves them all.
        /// </summary>
        const float ControlWidth = 150;

        /// <summary>
        /// Width the pause button is given where there is room for it, and the least it will be squeezed
        /// to where there is not. It is not a parameter and is sized to say so.
        /// </summary>
        const float PauseWidth = 200f;
        const float MinPauseWidth = 60f;

        /// <summary>
        /// Space either side of the rule separating pause from the parameters.
        /// </summary>
        const float RuleGap = 30f;

        /// <summary>
        /// Space between one column's controls and the next, and between one row and the next.
        /// </summary>
        const float ColumnGap = 20f;
        const float RowGap = 8f;

        /// <summary>
        /// Space between the controls and the left and right edges of the strip.
        /// </summary>
        const float EdgeMargin = 20f;

        /// <summary>
        /// Left inset of the collapse toggle and the strip's title, which sit closer to the edge than
        /// the controls do.
        /// </summary>
        const float HeaderInset = 6f;

        /// <summary>
        /// Space above and below everything, inside whatever frame the owner draws. A collapsed strip
        /// is meant to take almost no room, so it keeps only enough to clear the toggle.
        /// </summary>
        const float VerticalMargin = 6f;
        const float CollapsedMargin = 1f;

        const string RecordTooltip = "How much recent signal is kept for scrolling back through while paused.";

        /// <summary>
        /// Whether the controls are folded away, leaving only the toggle that brings them back.
        /// </summary>
        public bool Collapsed { get; set; }

        /// <summary>
        /// Draws the strip into the region the caller has opened, with <paramref name="columns"/> in order. Called
        /// once per frame.
        /// </summary>
        public void Draw(ImGuiWaveformPanel panel, IReadOnlyList<ControlStripColumn> columns)
        {
            var margin = Collapsed ? CollapsedMargin : VerticalMargin;
            ImGui.Dummy(new Vector2(0, margin));

            ImGui.Indent(HeaderInset);
            if (ImGui.ArrowButton("##collapse", Collapsed ? ImGuiDir.Up : ImGuiDir.Down))
                Collapsed = !Collapsed;

            ImGui.SameLine();
            ImGui.Text("Control Panel");
            ImGui.Unindent(HeaderInset);

            if (!Collapsed)
                DrawControls(panel, columns);

            // NB: an item rather than a cursor move, since nothing follows it to extend the content
            // extent and ImGui asserts when a cursor move is the last thing in a window.
            ImGui.Dummy(new Vector2(0, margin));
        }

        /// <summary>
        /// Lays the columns out as a wrapping grid, with pause kept to the right of a rule.
        /// </summary>
        /// <remarks>
        /// A table cannot wrap, so the controls are positioned outright. Every control takes one width, which
        /// makes the wrap arithmetic rather than measurement, and the column pitch is worked out once from a
        /// full row so that a partly filled last row keeps the columns of the rows above it rather than
        /// spreading its own few items across the whole width.
        /// </remarks>
        void DrawControls(ImGuiWaveformPanel panel, IReadOnlyList<ControlStripColumn> columns)
        {
            // NB: the flow is positioned outright rather than indented, so every horizontal distance here is
            // measured from one origin and the rule and pause go exactly where the layout reserved room for
            // them.
            var origin = ImGui.GetCursorScreenPos();
            var available = ImGui.GetContentRegionAvail().X;
            var usable = available - EdgeMargin * 2;
            var rightZone = RuleGap * 2 + ImGuiWaveformPanel.FrameWeight + PauseWidth;
            var flowWidth = Math.Max(ControlWidth, usable - rightZone);

            // NB: one control column is the floor for the flow, so a window narrow enough to reach it
            // leaves pause less than it asked for. It gives width up rather than being clipped.
            var pauseWidth = Math.Max(MinPauseWidth, Math.Min(
                PauseWidth,
                usable - flowWidth - RuleGap * 2 - ImGuiWaveformPanel.FrameWeight));

            var count = Math.Max(1, columns.Count);
            var columnsPerRow = Math.Max(1, (int)((flowWidth + ColumnGap) / (ControlWidth + ColumnGap)));
            columnsPerRow = Math.Min(columnsPerRow, count);
            var columnPitch = columnsPerRow > 1
                ? (flowWidth - columnsPerRow * ControlWidth) / (columnsPerRow - 1) + ControlWidth
                : ControlWidth;

            var style = ImGui.GetStyle();
            var rowPitch = ImGui.GetTextLineHeight() + style.ItemSpacing.Y + ImGui.GetFrameHeight() + RowGap;
            var rows = (count + columnsPerRow - 1) / columnsPerRow;
            var flowOrigin = new Vector2(origin.X + EdgeMargin, origin.Y);

            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var slot = new Vector2(
                    flowOrigin.X + i % columnsPerRow * columnPitch,
                    flowOrigin.Y + i / columnsPerRow * rowPitch);
                ImGui.SetCursorScreenPos(slot);

                // NB: under the column's position, so that two columns can use the same IDs for their own controls.
                ImGui.PushID(i);
                var label = column.Label;
                if (label.Length > 0)
                {
                    ImGui.Text(label);
                    ImGui.SetCursorScreenPos(new Vector2(
                        slot.X, slot.Y + ImGui.GetTextLineHeight() + style.ItemSpacing.Y));
                }
                column.Draw(ControlWidth);
                ImGui.PopID();
            }

            var pauseHeight = rowPitch - RowGap;
            var barHeight = ImGui.GetFrameHeight() / 2;
            var stackHeight = ImGui.GetTextLineHeight() + style.ItemSpacing.Y
                + barHeight + style.ItemSpacing.Y + pauseHeight;

            // NB: at one row the pause zone is taller than the controls beside it, so it sets the height.
            var flowHeight = Math.Max(rows * rowPitch - RowGap, stackHeight);
            var ruleX = MathF.Floor(flowOrigin.X + flowWidth + RuleGap);
            ImGui.GetWindowDrawList().AddRectFilled(
                new Vector2(ruleX, flowOrigin.Y),
                new Vector2(ruleX + ImGuiWaveformPanel.FrameWeight, flowOrigin.Y + flowHeight),
                ImGuiWaveformPanel.FrameColor);

            // NB: the history bar and pause travel together as one stack, centered: a narrow window wraps
            // the controls into several rows, and a pause button that grew with them would be absurdly tall.
            var stackX = ruleX + ImGuiWaveformPanel.FrameWeight + RuleGap;
            var stackY = flowOrigin.Y + (flowHeight - stackHeight) / 2;

            var held = panel.HistoryHeldSeconds;
            var span = panel.HistorySeconds;

            ImGui.SetCursorScreenPos(new Vector2(stackX, stackY));
            PauseButton(panel, new Vector2(pauseWidth, pauseHeight));

            ImGui.SetCursorScreenPos(new Vector2(
                stackX, stackY + pauseHeight + 2 * style.ItemSpacing.Y));
            ImGui.ProgressBar(
                span > 0 ? (float)(held / span) : 0f,
                new Vector2(pauseWidth, barHeight),
                string.Empty);
            ImGuiWidgets.Tooltip(RecordTooltip);

            ImGui.SetCursorScreenPos(new Vector2(stackX, stackY + pauseHeight + 2 * style.ItemSpacing.Y + ImGui.GetTextLineHeight()));
            ImGui.Text($"Record Length: {held:0.0} / {span:0.0} s");
            ImGuiWidgets.Tooltip(RecordTooltip);

            // NB: positioning everything outright leaves the parent's content extent where it was, so
            // the whole laid-out area is claimed in one go. It goes last, so that the cursor ends below
            // every row rather than below whichever item happened to be drawn last.
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(available, flowHeight));
        }

        static void PauseButton(ImGuiWaveformPanel panel, Vector2 buttonSize)
        {
            var paused = panel.Paused;
            if (paused)
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));

            if (ImGui.Button("Pause", buttonSize))
                panel.Paused = !paused;
            ImGuiWidgets.Tooltip("Freeze the display to study it. While paused, scroll back through the recent signal " +
                "and zoom in on it. Recording is not affected.\nKey: Space");

            if (paused)
                ImGui.PopStyleColor();
        }
    }
}
