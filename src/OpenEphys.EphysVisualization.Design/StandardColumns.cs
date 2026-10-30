using System;
using System.Collections.Generic;
using System.Numerics;
using Hexa.NET.ImGui;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Represents one column of a waveform viewer's control strip: a label, and the controls drawn under it.
    /// </summary>
    public readonly struct ControlStripColumn
    {
        readonly string? label;
        readonly Func<string>? labelSource;

        /// <summary>
        /// Initializes a new column.
        /// </summary>
        /// <param name="label">
        /// The label drawn at the head of the column, or an empty string for none, in which case the controls take
        /// the column's whole height.
        /// </param>
        /// <param name="draw">
        /// Draws the column's controls, once per frame, from the cursor, given the width in pixels they have to fill.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="label"/> or <paramref name="draw"/> is null.</exception>
        public ControlStripColumn(string label, Action<float> draw)
        {
            this.label = label ?? throw new ArgumentNullException(nameof(label));
            labelSource = null;
            Draw = draw ?? throw new ArgumentNullException(nameof(draw));
        }

        internal ControlStripColumn(Func<string> label, Action<float> draw)
        {
            this.label = null;
            labelSource = label;
            Draw = draw;
        }

        /// <summary>
        /// Gets the label drawn at the head of the column, or an empty string for none.
        /// </summary>
        public string Label => labelSource?.Invoke() ?? label ?? string.Empty;

        /// <summary>
        /// Gets the action that draws the column's controls, given the width in pixels they have to fill.
        /// </summary>
        public Action<float> Draw { get; }
    }

    /// <summary>
    /// Provides the control strip columns for the settings every waveform viewer has.
    /// </summary>
    /// <remarks>
    /// The plot's gestures (wheel, keys and drags) change these settings whether or not their columns are drawn, so
    /// a viewer that leaves a column out leaves its gestures acting on a setting nothing on screen shows. Every
    /// setting a gesture reaches should have its column drawn somewhere.
    /// </remarks>
    public sealed class StandardColumns
    {
        readonly ImGuiWaveformPanel panel;

        internal StandardColumns(ImGuiWaveformPanel panel)
        {
            this.panel = panel;
            Timebase = new("Timebase (s)", TimebaseControls);
            ChannelHeight = new("Chan. Height (px)", ChannelHeightControls);
            Range = new(RangeLabel, RangeControls);
            Palette = new("Palette", PaletteControls);
            Display = new(string.Empty, DisplayControls);
            All = new[] { Timebase, ChannelHeight, Range, Palette, Display };
        }

        /// <summary>
        /// Gets the column for the time the plot spans.
        /// </summary>
        public ControlStripColumn Timebase { get; }

        /// <summary>
        /// Gets the column for the height of a channel's row, and whether every channel is fitted to the plot.
        /// </summary>
        public ControlStripColumn ChannelHeight { get; }

        /// <summary>
        /// Gets the column for the amplitude a channel's row spans, which in the heatmap is the colors' range and
        /// threshold.
        /// </summary>
        public ControlStripColumn Range { get; }

        /// <summary>
        /// Gets the column for the palette the channels are drawn in.
        /// </summary>
        public ControlStripColumn Palette { get; }

        /// <summary>
        /// Gets the column for whether the heatmap, the cursors and clipping are shown.
        /// </summary>
        public ControlStripColumn Display { get; }

        /// <summary>
        /// Gets every standard column, in the order a viewer draws them by default.
        /// </summary>
        public IReadOnlyList<ControlStripColumn> All { get; }

        void TimebaseControls(float width)
        {
            var timebase = panel.Timebase;
            if (InputDoubleCombo("##timebase", ref timebase, panel.StandardTimeBases, width,
                "During live display, the time one sweep takes to cross the plot, in seconds. When paused, the time " +
                "the plot window shows from record history. A shorter time shows faster events in more detail.\n" +
                "Keys: Ctrl+A longer, Ctrl+D shorter, T longest\n" +
                "On the plot: Ctrl+Shift+Wheel, or Ctrl+Drag left and right or right-drag a box while paused"))
                panel.Timebase = timebase;
        }

        void ChannelHeightControls(float width)
        {
            const string FitLabel = "Fit";
            var inner = ImGui.GetStyle().ItemInnerSpacing.X;
            var fitWidth = ImGui.GetFrameHeight() + inner + ImGui.CalcTextSize(FitLabel).X;
            ImGui.SetNextItemWidth(width - fitWidth - inner);
            var previousHeight = panel.ChannelHeight;
            var channelHeight = previousHeight;
            const float Step = ImGuiWaveformPanel.ChannelHeightStep;
            var single = panel.SingleChannel;
            ImGui.BeginDisabled(panel.FitChannels || single);
            if (ImGui.InputFloat("##channelHeight", ref channelHeight, Step, Step,
                channelHeight % 1 == 0 ? "%.0f" : "%.1f"))
            {
                // NB: ImGui reports the buttons only as a change of one step.
                if (channelHeight == previousHeight + Step)
                    channelHeight = ImGuiWaveformPanel.StepChannelHeight(previousHeight, 1);
                else if (channelHeight == previousHeight - Step)
                    channelHeight = ImGuiWaveformPanel.StepChannelHeight(previousHeight, -1);
                panel.ChannelHeight = channelHeight;
            }
            ImGuiWidgets.Tooltip("Height of each channel's row, in pixels.\n" +
                "Keys: Ctrl+W taller, Ctrl+S shorter\n" +
                "On the plot: Ctrl+Wheel, or Ctrl+Drag up and down or right-drag a box while paused",
                single ? SingleChannelHint : "Turn Fit off to set the height yourself.");
            ImGui.EndDisabled();
            ImGui.SameLine(0, inner);
            ImGui.BeginDisabled(single);
            var fit = panel.FitChannels;
            if (ImGui.Checkbox(FitLabel + "##fitChannels", ref fit))
                panel.FitChannels = fit;
            ImGuiWidgets.Tooltip("Fit every channel into the plot's height, so that none is out of view.\nKey: F",
                SingleChannelHint);
            ImGui.EndDisabled();
        }

        string RangeLabel()
        {
            var name = panel.HeatmapDrawn ? "Color" : "Range";
            return string.IsNullOrEmpty(panel.Unit) ? name : $"{name} ({panel.Unit})";
        }

        // NB: follows what the plot draws rather than the Heatmap checkbox. An expanded channel is a trace even with
        // the heatmap on, and a threshold set there would have no visible effect until the channel was collapsed.
        void RangeControls(float width)
        {
            if (panel.HeatmapDrawn)
            {
                ColorControls(width);
                return;
            }

            var range = panel.Range;
            if (InputDoubleCombo("##range", ref range, panel.StandardRanges, width,
                "Amplitude each channel's row spans. A smaller range magnifies the signal.\n" + RangeKeys))
                panel.Range = range;
        }

        // NB: the range sets the colors' bright end, so it stays in view, at the end of the bar it bounds. The bar
        // is half height so that the threshold's value fits under it within the slot.
        void ColorControls(float width)
        {
            var corner = ImGui.GetCursorScreenPos();
            var style = ImGui.GetStyle();
            var rangeWidth = ImGui.CalcTextSize(WidestRange).X + 2 * style.FramePadding.X + ImGui.GetFrameHeight();
            var barWidth = width - rangeWidth - style.ItemInnerSpacing.X;

            var threshold = (float)panel.ColorThreshold;
            if (ImGuiThresholdSlider.Draw(
                "##threshold", Icefire.Map, (float)panel.Range, ref threshold,
                barWidth, ImGui.GetFrameHeight() / 2,
                tooltip: "Signals smaller than this show as black, so that only larger activity is colored. Drag to set."))
            {
                panel.ColorThreshold = threshold;
            }

            ImGui.SetCursorScreenPos(new Vector2(corner.X + width - rangeWidth, corner.Y));
            var range = panel.Range;
            if (InputDoubleCombo("##range", ref range, panel.StandardRanges, rangeWidth,
                "Amplitude at which the colors are brightest. A smaller range brings out smaller signals.\n" + RangeKeys))
                panel.Range = range;
        }

        void PaletteControls(float width)
        {
            var swatch = panel.Colors.Palette == ColorPalette.Custom
                ? ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X
                : 0f;
            ImGui.SetNextItemWidth(width - swatch);
            PaletteCombo();
            if (panel.Colors.Palette == ColorPalette.Custom)
            {
                ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
                var customColor = panel.Colors.CustomColor;
                if (ImGui.ColorEdit3("##customColor", ref customColor,
                    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoTooltip))
                    panel.Colors.CustomColor = customColor;
                ImGuiWidgets.Tooltip("The color every channel is drawn in. Click to choose another.");
            }
        }

        static readonly (ColorPalette Palette, string Name)[] Palettes =
        {
            (ColorPalette.OpenEphysGui, "Open Ephys GUI"),
            (ColorPalette.Glasbey, "Glasbey"),
            (ColorPalette.Custom, "Custom"),
        };

        void PaletteCombo()
        {
            var current = panel.Colors.Palette;
            var open = ImGui.BeginCombo("##palette", Array.Find(Palettes, p => p.Palette == current).Name ?? string.Empty);
            ImGuiWidgets.Tooltip("Colors the channels are drawn in while they are not grouped: the Open Ephys GUI's, " +
                "colors chosen to be told apart, or one color you choose.");
            if (open)
            {
                foreach (var (palette, name) in Palettes)
                {
                    if (ImGui.Selectable(name, palette == current))
                        panel.Colors.Palette = palette;
                    if (palette == current)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
        }

        // NB: two lines of checkboxes in the height of a label and a control, so their frames are made shorter.
        void DisplayControls(float width)
        {
            var spacing = ImGui.GetStyle().ItemSpacing.Y;
            var line = (ImGui.GetTextLineHeight() + ImGui.GetFrameHeight()) / 2;
            var padding = ImGui.GetStyle().FramePadding;
            var slot = ImGui.GetCursorScreenPos();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,
                new Vector2(padding.X, Math.Max(0, (line - ImGui.GetFontSize()) / 2)));

            ImGui.BeginDisabled(panel.SingleChannel);
            var showHeatmap = panel.ShowHeatmap;
            if (ImGui.Checkbox("Heatmap", ref showHeatmap))
                panel.ShowHeatmap = showHeatmap;
            ImGuiWidgets.Tooltip("Show each channel as a row of color, brighter for larger signals, instead of as a " +
                "trace, so that patterns of activity across neighboring channels stand out. Double-click a row to read " +
                "it as a trace.\nKey: H", SingleChannelHint);
            ImGui.EndDisabled();

            ImGui.SameLine();
            var showCursors = panel.ShowCursors;
            if (ImGui.Checkbox("Cursors", ref showCursors))
                panel.ShowCursors = showCursors;
            ImGuiWidgets.Tooltip("Show cursors for measuring time and amplitude on the selected channel, with a table " +
                "of their readings. Click a channel's label or trace to choose the channel they read.\nKey: C");

            ImGui.SetCursorScreenPos(new Vector2(slot.X, slot.Y + line + spacing));
            var showClipping = panel.ShowClipping;
            if (ImGui.Checkbox("Clipping", ref showClipping))
                panel.ShowClipping = showClipping;
            ImGuiWidgets.Tooltip("Warn when a channel's signal reaches the limit of the amplifier's range, by " +
                "flashing CLIPPING in the plot and the channel's label. Hidden channels do not warn.");
            ImGui.PopStyleVar();
        }

        // NB: fixed rather than measured off the presets, which move with the range and would make the bar jump. %g
        // prints any range of the sequence in seven characters or fewer, turning to an exponent below 1e-4 and from
        // 1e6.
        const string WidestRange = "0.00025";

        const string RangeKeys = "Keys: Alt+W smaller, Alt+S larger\nOn the plot: Alt+Wheel, or Alt+Drag up and down";

        const string SingleChannelHint = "There is only one channel, which is always shown expanded.";

        // NB: the preset arrow sits beside the input, so the input takes the slot less the arrow, and both carry
        // the tooltip, which only ever belongs to one item.
        static bool InputDoubleCombo(
            string label, ref double value, IReadOnlyList<double> comboItems, float width, string tooltip)
        {
            var changed = false;
            var editValue = value;
            ImGui.SetNextItemWidth(width - ImGui.GetFrameHeight());
            ImGui.InputDouble(label, ref editValue, "%g");
            if (changed = ImGui.IsItemDeactivatedAfterEdit())
                value = editValue;
            ImGuiWidgets.Tooltip(tooltip);
            ImGui.SameLine(0, 0);

            var comboFlags = ImGuiComboFlags.NoPreview | ImGuiComboFlags.PopupAlignLeft;
            var open = ImGui.BeginCombo(label + "C", string.Empty, comboFlags);
            ImGuiWidgets.Tooltip(tooltip);
            if (open)
            {
                for (int i = 0; i < comboItems.Count; i++)
                {
                    var isSelected = value == comboItems[i];
                    if (ImGui.Selectable(comboItems[i].ToString("G"), isSelected))
                    {
                        value = comboItems[i];
                        changed = true;
                    }
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            return changed;
        }
    }
}
