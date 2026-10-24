using System;
using System.Numerics;
using Hexa.NET.ImGui;

namespace OpenEphys.EphysVisualization.Design
{
    // NB: filled rects on whole pixels. AddRect and AddLine draw half a pixel off and anti-alias, which blurs a 1 px
    // line.
    internal static class PixelRect
    {
        /// <summary>
        /// Fills the rectangle from <paramref name="min"/> to <paramref name="max"/>, to the same pixels
        /// <see cref="Outline"/> draws its border on.
        /// </summary>
        public static void Fill(ImDrawListPtr draw, Vector2 min, Vector2 max, uint color) =>
            draw.AddRectFilled(new Vector2(MathF.Floor(min.X), MathF.Floor(min.Y)),
                new Vector2(MathF.Floor(max.X), MathF.Floor(max.Y)), color);

        /// <summary>
        /// Outlines the rectangle from <paramref name="min"/> to <paramref name="max"/>, inside it, with lines
        /// <paramref name="weight"/> pixels wide.
        /// </summary>
        public static void Outline(ImDrawListPtr draw, Vector2 min, Vector2 max, uint color, float weight = 1)
        {
            var l = MathF.Floor(min.X);
            var r = MathF.Floor(max.X);
            var t = MathF.Floor(min.Y);
            var b = MathF.Floor(max.Y);
            draw.AddRectFilled(new Vector2(l, t), new Vector2(r, t + weight), color);
            draw.AddRectFilled(new Vector2(l, b - weight), new Vector2(r, b), color);
            draw.AddRectFilled(new Vector2(l, t), new Vector2(l + weight, b), color);
            draw.AddRectFilled(new Vector2(r - weight, t), new Vector2(r, b), color);
        }
    }
}
