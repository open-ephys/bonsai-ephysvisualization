using System;
using System.Numerics;
using Hexa.NET.ImGui;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Text drawn over a plot: readings, labels and the boxes they sit in.
    /// </summary>
    internal static class PlotText
    {
        /// <summary>
        /// Fill behind text drawn over traces, dark enough to read against any of them.
        /// </summary>
        public static readonly uint LabelBackground = ImGuiPalette.WithAlpha(ImGuiPalette.Black, 0xBB);

        /// <summary>
        /// Writes <paramref name="text"/> at <paramref name="corner"/> in a box framed like the cursor table, or
        /// only filled where <paramref name="outline"/> is false.
        /// </summary>
        /// <remarks>
        /// The cursor table is a bordered child window, so the box takes its rounding and border color from the
        /// style, and the overlays on the plot read as one set.
        /// </remarks>
        public static void Framed(ImDrawListPtr draw, Vector2 corner, string text, bool outline = true)
        {
            var pad = ImGui.GetStyle().FramePadding;
            var rounding = ImGui.GetStyle().ChildRounding;
            var size = ImGui.CalcTextSize(text);
            draw.AddRectFilled(corner - pad, corner + size + pad, LabelBackground, rounding);
            if (outline)
                draw.AddRect(corner - pad, corner + size + pad, ImGui.GetColorU32(ImGuiCol.Border), rounding);
            draw.AddText(corner, ImGui.GetColorU32(ImGuiCol.Text), text);
        }

        /// <summary>
        /// Rounds <paramref name="value"/> to three significant figures.
        /// </summary>
        /// <remarks>
        /// Three significant figures are used because it covers full dynamic range of visual inspection of a
        /// screen. Math.Round with a number of decimal places returns the double closest to the rounded
        /// decimal, so it prints cleanly. Scaling by a power of ten below one and back doesn't, because those
        /// powers aren't exact in binary and leave a tail of digits like 0.30000000000000004. Math.Round
        /// can't take negative decimal places, though, so values of 1000 and above are scaled by a power of
        /// ten above one instead, which is exact.
        /// </remarks>
        public static double Significant(double value)
        {
            if (value == 0)
                return 0;

            var decimals = 2 - (int)Math.Floor(Math.Log10(Math.Abs(value)));
            if (decimals >= 0)
                return Math.Round(value, Math.Min(15, decimals));

            var scale = Math.Pow(10, -decimals);
            return Math.Round(value / scale) * scale;
        }
    }
}
