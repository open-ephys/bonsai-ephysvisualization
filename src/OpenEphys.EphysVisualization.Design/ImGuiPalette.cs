using System.Collections.Generic;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Provides the house color swatches layered on top of Dear ImGui's dark theme, in Dear ImGui's packed
    /// <c>0xAABBGGRR</c> format. Each summary gives the color as sRGB hex.
    /// </summary>
    public static class ImGuiPalette
    {
        /// <summary>#000000</summary>
        public static readonly uint Black = 0xFF000000u;
        /// <summary>#FFFFFF</summary>
        public static readonly uint White = 0xFFFFFFFFu;
        /// <summary>#FF0000</summary>
        public static readonly uint Red = 0xFF0000FFu;
        /// <summary>#00FF00</summary>
        public static readonly uint Green = 0xFF00FF00u;
        /// <summary>#0000FF</summary>
        public static readonly uint Blue = 0xFFFF0000u;
        /// <summary>#FFFF00</summary>
        public static readonly uint Yellow = 0xFF00FFFFu;

        /// <summary>#FF595E</summary>
        public static readonly uint VibrantCoral = 0xFF5E59FFu;
        /// <summary>#FFCA3A</summary>
        public static readonly uint GoldenPollen = 0xFF3ACAFFu;
        /// <summary>#8AC926</summary>
        public static readonly uint YellowGreen = 0xFF26C98Au;
        /// <summary>#1982C4</summary>
        public static readonly uint SteelBlue = 0xFFC48219u;
        /// <summary>#6A4C93</summary>
        public static readonly uint DustyGrape = 0xFF934C6Au;

        /// <summary>#FFBE0B</summary>
        public static readonly uint AmberGold = 0xFF0BBEFFu;
        /// <summary>#FB5607</summary>
        public static readonly uint BlazeOrange = 0xFF0756FBu;
        /// <summary>#FF006E</summary>
        public static readonly uint NeonPink = 0xFF6E00FFu;
        /// <summary>#8338EC</summary>
        public static readonly uint BlueViolet = 0xFFEC3883u;
        /// <summary>#3A86FF</summary>
        public static readonly uint AzureBlue = 0xFFFF863Au;
        /// <summary>#0056E0</summary>
        public static readonly uint RoyalAzure = 0xFFE05600u;
        /// <summary>#0046B8</summary>
        public static readonly uint CobaltBlue = 0xFFB84600u;
        /// <summary>#29BF12</summary>
        public static readonly uint BrightFern = 0xFF12BF29u;

        /// <summary>#303030</summary>
        public static readonly uint Grey0x30 = 0xFF303030u;
        /// <summary>#4D4D4D</summary>
        public static readonly uint Grey0x4D = 0xFF4D4D4Du;
        /// <summary>#555555</summary>
        public static readonly uint Grey0x55 = 0xFF555555u;
        /// <summary>#777777</summary>
        public static readonly uint Grey0x77 = 0xFF777777u;
        /// <summary>#808080</summary>
        public static readonly uint Grey0x80 = 0xFF808080u;
        /// <summary>#888888</summary>
        public static readonly uint Grey0x88 = 0xFF888888u;
        /// <summary>#999999</summary>
        public static readonly uint Grey0x99 = 0xFF999999u;
        /// <summary>#CCCCCC</summary>
        public static readonly uint Grey0xCC = 0xFFCCCCCCu;

        /// <summary>
        /// Gets the Open Ephys GUI's classic sixteen-color waveform scope channel palette.
        /// </summary>
        public static IReadOnlyList<uint> OpenEphysGuiLfp { get; } = new[]
        {
            0xFF24B9E0u, 0xFFB6D2D6u, 0xFF2177F3u, 0xFFA89DBAu,
            0xFF2425EDu, 0xFF4F7AB3u, 0xFFAB2ED9u, 0xFFC48BD9u,
            0xFFFF1F65u, 0xFFB56F8Du, 0xFFFF7530u, 0xFFE0C6B8u,
            0xFF9CE374u, 0xFF9B9E96u, 0xFF00AD52u, 0xFF20637Du,
        };

        /// <summary>
        /// Returns <paramref name="color"/> with its alpha byte replaced.
        /// </summary>
        /// <param name="color">A color in packed <c>0xAABBGGRR</c> format.</param>
        /// <param name="alpha">The new alpha, from transparent at 0 to opaque at 255.</param>
        public static uint WithAlpha(uint color, byte alpha) =>
            (color & 0x00FFFFFFu) | ((uint)alpha << 24);
    }
}
