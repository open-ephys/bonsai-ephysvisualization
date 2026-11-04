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
    internal sealed class ImGuiWaveformControlStrip
    {
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
            ImGuiWidgets.Tooltip(Collapsed ? "Show the control panel." : "Hide the control panel.");

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
        /// A table cannot wrap, so the controls are positioned outright.
        /// </remarks>
        void DrawControls(ImGuiWaveformPanel panel, IReadOnlyList<ControlStripColumn> columns)
        {
            var origin = ImGui.GetCursorScreenPos();
            var available = ImGui.GetContentRegionAvail().X;
            var usable = available - EdgeMargin * 2;
            var rightZone = RuleGap * 2 + ImGuiWaveformPanel.FrameWeight + PauseWidth;

            var labels = new string[columns.Count];
            var widths = new float[columns.Count];
            var widest = 0f;
            for (int i = 0; i < columns.Count; i++)
            {
                labels[i] = columns[i].Label;
                widths[i] = Math.Max(columns[i].Width, ImGui.CalcTextSize(labels[i]).X);
                widest = Math.Max(widest, widths[i]);
            }

            var flowWidth = Math.Max(widest, usable - rightZone);
            var pauseWidth = Math.Max(MinPauseWidth, Math.Min(
                PauseWidth,
                usable - flowWidth - RuleGap * 2 - ImGuiWaveformPanel.FrameWeight));

            var grid = GridColumns(widths, flowWidth, ColumnGap);
            var columnsPerRow = grid.Length;
            var gridX = new float[columnsPerRow];
            for (int j = 1; j < columnsPerRow; j++)
                gridX[j] = gridX[j - 1] + grid[j - 1] + ColumnGap;

            var style = ImGui.GetStyle();
            var rowPitch = ImGui.GetTextLineHeight() + style.ItemSpacing.Y + ImGui.GetFrameHeight() + RowGap;
            var rows = (Math.Max(1, columns.Count) + columnsPerRow - 1) / columnsPerRow;
            var flowOrigin = new Vector2(origin.X + EdgeMargin, origin.Y);

            for (int i = 0; i < columns.Count; i++)
            {
                var slot = new Vector2(
                    flowOrigin.X + gridX[i % columnsPerRow],
                    flowOrigin.Y + i / columnsPerRow * rowPitch);
                ImGui.SetCursorScreenPos(slot);

                // NB: under the column's position, so that two columns can use the same IDs for their own controls.
                ImGui.PushID(i);
                if (labels[i].Length > 0)
                {
                    ImGui.Text(labels[i]);
                    ImGui.SetCursorScreenPos(new Vector2(
                        slot.X, slot.Y + ImGui.GetTextLineHeight() + style.ItemSpacing.Y));
                }
                columns[i].Draw(grid[i % columnsPerRow]);
                ImGui.PopID();
            }

            var pauseHeight = rowPitch - RowGap;
            var barHeight = ImGui.GetFrameHeight() / 2;
            var stackHeight = ImGui.GetTextLineHeight() + style.ItemSpacing.Y
                + barHeight + style.ItemSpacing.Y + pauseHeight;

            var flowHeight = Math.Max(rows * rowPitch - RowGap, stackHeight);
            var ruleX = MathF.Floor(flowOrigin.X + flowWidth + RuleGap);
            ImGui.GetWindowDrawList().AddRectFilled(
                new Vector2(ruleX, flowOrigin.Y),
                new Vector2(ruleX + ImGuiWaveformPanel.FrameWeight, flowOrigin.Y + flowHeight),
                ImGuiWaveformPanel.FrameColor);

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

        /// <summary>
        /// The widths of the grid's places, as many to a row as fit in <paramref name="flowWidth"/> with
        /// <paramref name="gap"/> between them, each the widest of <paramref name="widths"/> that lands in it, or one
        /// place where even two do not fit.
        /// </summary>
        internal static float[] GridColumns(IReadOnlyList<float> widths, float flowWidth, float gap)
        {
            for (int perRow = Math.Max(1, widths.Count); ; perRow--)
            {
                var grid = new float[perRow];
                for (int i = 0; i < widths.Count; i++)
                    grid[i % perRow] = Math.Max(grid[i % perRow], widths[i]);

                var total = (perRow - 1) * gap;
                foreach (var width in grid)
                    total += width;
                if (perRow == 1 || total <= flowWidth)
                    return grid;
            }
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
