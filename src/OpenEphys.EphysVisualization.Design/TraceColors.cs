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
        /// Shades of one chosen color.
        /// </summary>
        Custom,
    }

    /// <summary>
    /// The colors channels are drawn in: a palette, and the group each channel takes its color from.
    /// </summary>
    internal sealed class TraceColors
    {
        const int PaletteSteps = 16;

        readonly WaveformScope settings;

        uint[] packed = Array.Empty<uint>();

        /// <param name="settings">The node whose palette and custom color are read and written.</param>
        public TraceColors(WaveformScope settings) => this.settings = settings;

        /// <summary>
        /// The group a channel takes its color from, which channels sharing a color share. Each channel is its
        /// own group unless the owner says otherwise.
        /// </summary>
        /// <remarks>
        /// Read for every channel every frame, so an assignment that reads its owner's settings live never has to
        /// be told they changed.
        /// </remarks>
        public Func<int, int> GroupOf { get; set; } = channel => channel;

        /// <summary>
        /// Palette the colors are drawn from.
        /// </summary>
        public ColorPalette Palette
        {
            get => settings.Palette;
            set => settings.Palette = value;
        }

        /// <summary>
        /// Hue the <see cref="ColorPalette.Custom"/> palette is built from.
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
        /// The color channel <paramref name="channel"/> is drawn in.
        /// </summary>
        /// <remarks>
        /// Read by the trace, its outline and its label's highlight alike, so they always agree.
        /// </remarks>
        public Vector4 Of(int channel) => Group(GroupOf(channel));

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
                var c = Of(ImGuiWaveformPanel.ChannelOf(rowChannels, i));
                packed[i] = (uint)(c.X * 255) | (uint)(c.Y * 255) << 8 | (uint)(c.Z * 255) << 16 | 0xFFu << 24;
            }

            return packed;
        }

        /// <summary>
        /// The color for channel group <paramref name="group"/> under the selected <see cref="Palette"/>.
        /// </summary>
        /// <remarks>
        /// In <see cref="ColorPalette.Custom"/>, groups are shades of the picked hue, taken out of order from
        /// <see cref="PaletteSteps"/> saturation and value steps so that neighboring groups differ clearly.
        /// Group 0 is the picked color itself.
        /// </remarks>
        Vector4 Group(int group)
        {
            if (Palette == ColorPalette.OpenEphysGui)
                return ImGui.ColorConvertU32ToFloat4(ImGuiPalette.OpenEphysGuiLfp[group % ImGuiPalette.OpenEphysGuiLfp.Count]);

            float hue = 0, saturation = 0, value = 0;
            ImGui.ColorConvertRGBtoHSV(CustomColor.X, CustomColor.Y, CustomColor.Z, ref hue, ref saturation, ref value);

            var step = group * 7 % PaletteSteps;
            var fraction = step / (float)(PaletteSteps - 1);
            saturation = Math.Min(1f, saturation + 0.4f * fraction);
            value *= 1f - 0.6f * fraction;

            float r = 0, g = 0, b = 0;
            ImGui.ColorConvertHSVtoRGB(hue, saturation, value, ref r, ref g, ref b);
            return new Vector4(r, g, b, 1f);
        }
    }
}
