using System;
using System.Numerics;
using Hexa.NET.ImGui;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Specifies the palette a waveform viewer draws its channels in.
    /// </summary>
    public enum ColorPalette
    {
        /// <summary>
        /// The colors the Open Ephys GUI draws channels in.
        /// </summary>
        OpenEphysGui,

        /// <summary>
        /// The colors channel groups are drawn in, chosen to be told apart: Glasbey's category10 palette, without its
        /// grey and its colors too dark for the plot.
        /// </summary>
        Glasbey,

        /// <summary>
        /// One chosen color.
        /// </summary>
        Custom,
    }

    /// <summary>
    /// The colors channels are drawn in: a palette, and the group each channel takes its color from.
    /// </summary>
    internal sealed class TraceColors
    {
        readonly WaveformScopeBuilder settings;

        uint[] packed = Array.Empty<uint>();

        /// <param name="settings">The node whose palette and custom color are read and written.</param>
        public TraceColors(WaveformScopeBuilder settings) => this.settings = settings;

        /// <summary>
        /// The group a channel takes its color from, which channels sharing a color share, or null for each row
        /// to take the next color of the palette.
        /// </summary>
        /// <remarks>
        /// Groups take their colors from <see cref="ImGuiPalette.Categorical"/> whatever the palette, which is there
        /// to tell neighboring rows apart rather than groups. Without one, the colors follow the rows rather than the
        /// channels, whichever channels an owner's order puts side by side. Read for every channel every frame, so an
        /// assignment that reads its owner's settings live never has to be told they changed.
        /// </remarks>
        public Func<int, int>? GroupOf { get; set; }

        /// <summary>
        /// Palette the colors are drawn from.
        /// </summary>
        public ColorPalette Palette
        {
            get => settings.Palette;
            set => settings.Palette = value;
        }

        /// <summary>
        /// Color the <see cref="ColorPalette.Custom"/> palette draws every row in.
        /// </summary>
        public Vector3 CustomColor
        {
            get
            {
                var color = settings.CustomColor;
                return new(color.R / 255f, color.G / 255f, color.B / 255f);
            }
            set => settings.CustomColor = System.Drawing.Color.FromArgb(Byte(value.X), Byte(value.Y), Byte(value.Z));
        }

        static int Byte(float component) => (int)Math.Round(Math.Max(0, Math.Min(1, component)) * 255);

        /// <summary>
        /// The color row <paramref name="row"/>, which shows channel <paramref name="channel"/>, is drawn in.
        /// </summary>
        /// <remarks>
        /// Read by the trace, its outline and its label's highlight alike, so they always agree.
        /// </remarks>
        public Vector4 Of(int row, int channel) => GroupOf is { } group ? Group(group(channel)) : Row(row);

        // NB: neutral, so that the groups stand out, but light enough to read as a trace on the black plot.
        static readonly uint Ungrouped = ImGuiPalette.Grey0xCC;

        /// <summary>
        /// The colors of the first <paramref name="count"/> rows, each its channel's, packed as RGBA, for drawing in
        /// bulk. The array is reused from call to call.
        /// </summary>
        /// <param name="count">How many rows.</param>
        /// <param name="rowChannels">The channel each row shows, or null for each row showing its own index.</param>
        public uint[] Packed(int count, int[]? rowChannels)
        {
            if (packed.Length != count)
                packed = new uint[count];

            for (int i = 0; i < count; i++)
            {
                var c = Of(i, ImGuiWaveformPanel.ChannelOf(rowChannels, i));
                packed[i] = (uint)(c.X * 255) | (uint)(c.Y * 255) << 8 | (uint)(c.Z * 255) << 16 | 0xFFu << 24;
            }

            return packed;
        }

        /// <summary>
        /// The color for channel group <paramref name="group"/>, or a neutral grey for a negative group, which is none.
        /// Groups past the last of <see cref="ImGuiPalette.Categorical"/> reuse its colors in order.
        /// </summary>
        public Vector4 Group(int group) => ImGui.ColorConvertU32ToFloat4(
            group < 0 ? Ungrouped : ImGuiPalette.Categorical[group % ImGuiPalette.Categorical.Count]);

        /// <summary>
        /// The color for row <paramref name="row"/> under the selected <see cref="Palette"/>.
        /// </summary>
        Vector4 Row(int row) => Palette switch
        {
            ColorPalette.Custom => new Vector4(CustomColor, 1f),
            ColorPalette.Glasbey => ImGui.ColorConvertU32ToFloat4(ImGuiPalette.Categorical[row % ImGuiPalette.Categorical.Count]),
            _ => ImGui.ColorConvertU32ToFloat4(ImGuiPalette.OpenEphysGuiLfp[row % ImGuiPalette.OpenEphysGuiLfp.Count]),
        };
    }
}
