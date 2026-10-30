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
        /// Gets the Open Ephys GUI's classic sixteen-color LFP viewer channel palette.
        /// </summary>
        public static IReadOnlyList<uint> OpenEphysGuiLfp { get; } = new[]
        {
            0xFF24B9E0u, 0xFFB6D2D6u, 0xFF2177F3u, 0xFFA89DBAu,
            0xFF2425EDu, 0xFF4F7AB3u, 0xFFAB2ED9u, 0xFFC48BD9u,
            0xFFFF1F65u, 0xFFB56F8Du, 0xFFFF7530u, 0xFFE0C6B8u,
            0xFF9CE374u, 0xFF9B9E96u, 0xFF00AD52u, 0xFF20637Du,
        };

        /// <summary>
        /// Gets the colors groups of channels are drawn in, chosen to be told apart from each other, from a neutral grey
        /// and on a black background.
        /// </summary>
        /// <remarks>
        /// Glasbey's category10 palette from colorcet (https://colorcet.holoviz.org, CC BY 4.0): Tableau 10 without its
        /// grey, then the further Glasbey colors with a CIELAB lightness of at least 55 and chroma of at least 35, so
        /// that none is too dark for the plot or too grey to tell from a channel in no group.
        /// </remarks>
        internal static IReadOnlyList<uint> Categorical { get; } = new[]
        {
            0xFFB4771Fu, 0xFF0E7FFFu, 0xFF2CA02Cu, 0xFF2827D6u, 0xFFBD6794u, 0xFF4B568Cu, 0xFFC277E3u, 0xFF22BDBCu,
            0xFFCFBE17u, 0xFFA9FF0Fu, 0xFFFFBCBCu, 0xFF9AFFFFu, 0xFF00FF91u, 0xFF4489ADu, 0xFF766FFFu, 0xFFFF8C79u,
            0xFF60BFFFu, 0xFF8D01FFu, 0xFFFF88CDu, 0xFF46D61Du, 0xFFFFAD00u, 0xFFB1D400u, 0xFF00958Bu, 0xFF00DCFFu,
            0xFF79AB00u, 0xFF76E3A6u, 0xFFB69EFFu, 0xFFD607FFu, 0xFF0249FFu, 0xFF009CD9u, 0xFF759AFFu, 0xFF55B781u,
            0xFFFF94FDu, 0xFFD38D9Du, 0xFF436DDAu, 0xFF4FFFDFu, 0xFF572BFFu, 0xFFFDE500u, 0xFF8CD568u, 0xFF66A2D6u,
            0xFFCE69C1u, 0xFFEDAC88u, 0xFFE6AAD2u, 0xFFDBFD00u, 0xFF00D391u, 0xFF0079CEu, 0xFFFF5AF9u, 0xFF64FE00u,
            0xFFA163FFu, 0xFFF18C01u, 0xFFA0AC14u, 0xFF6DDDDBu, 0xFFC5972Du, 0xFFFF74A9u, 0xFF5EBB27u, 0xFF00B758u,
            0xFFA7FFCBu, 0xFF94BDFFu, 0xFFFFC90Fu, 0xFF6BA56Au, 0xFF00DCCBu, 0xFF1BBCEBu, 0xFFD29CEBu, 0xFF32A1B1u,
            0xFF936CCAu, 0xFF8A8CE5u, 0xFFCB8BC7u, 0xFF98D3A5u, 0xFF8CDFFFu, 0xFF4D53E3u, 0xFF269FFFu, 0xFF5785C8u,
            0xFFFFA1A3u, 0xFFA7F297u, 0xFFD69467u, 0xFF57B4D0u, 0xFF418E8Eu, 0xFF3C966Au, 0xFF67C6ACu, 0xFF00B092u,
            0xFFDAE32Cu, 0xFF5379FFu, 0xFF00E94Fu, 0xFFD47688u, 0xFFDAFFA5u, 0xFF786FD8u, 0xFF72FF97u, 0xFFC953DCu,
            0xFF8EBE58u,
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
