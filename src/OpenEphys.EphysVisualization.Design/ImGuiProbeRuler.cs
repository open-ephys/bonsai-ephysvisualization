using Hexa.NET.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenEphys.EphysVisualization.Design
{
    internal class ImGuiProbeRuler
    {
        readonly record struct RulerLine(Vector2 Start, Vector2 End);
        enum PlacementState { None, Armed, Dragging, ClickMode }

        readonly List<RulerLine> locked = new();
        PlacementState placement = PlacementState.None;
        Vector2 draftStart;
        Vector2 draftStartScreen;
        Vector2 draftEnd;
        int selectedIdx = -1;

        public string Units { get; set; } = "um";

        static readonly uint ColorLine = ImGuiPalette.AzureBlue;
        static readonly uint ColorSelected = ImGuiPalette.Yellow;
        static readonly uint ColorLabel = ImGuiPalette.White;
        static readonly uint ColorLabelBg = ImGuiPalette.WithAlpha(ImGuiPalette.Black, 0xBB);
        static readonly uint ColorTickLabel = ImGuiPalette.Grey0xCC;
        const float LineWidth = 1.5f;
        const float LabelPad = 3f;
        const float HoverDist = 15f;
        const float MajorTickHalfLen = 8f;
        const float MinorTickHalfLen = 4f;
        const int MinorTicksPerMajor = 5;

        /// <summary>
        /// Whether a ruler is armed or partly placed, in which case it takes the clicks in the view.
        /// </summary>
        public bool Placing => placement != PlacementState.None;

        public bool HasSelection => selectedIdx >= 0;

        public int Count => locked.Count;

        /// <summary>
        /// Arms one ruler. The next press in the view starts it, and letting go after a drag, or a second click,
        /// sets it.
        /// </summary>
        public void Arm() => placement = PlacementState.Armed;

        public void Cancel() => placement = PlacementState.None;

        public void Deselect() => selectedIdx = -1;

        public void RemoveSelected()
        {
            if (selectedIdx < 0) return;
            locked.RemoveAt(selectedIdx);
            selectedIdx = -1;
        }

        public void RemoveAll()
        {
            locked.Clear();
            selectedIdx = -1;
        }

        /// <summary>
        /// Call each frame the view is drawn. Returns whether the ruler took this frame's left click, which is then
        /// not also a click on the contacts.
        /// </summary>
        public bool HandleInput(Vector2 mouseProbeCoord, bool inCanvas,
            Vector2 mouseScreenPos, float viewLeft, float viewBot,
            float xLow, float yLow, float scaleXY)
        {
            if (placement != PlacementState.None)
                draftEnd = mouseProbeCoord;

            var clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            switch (placement)
            {
                case PlacementState.None:
                    var near = inCanvas ? Nearest(mouseScreenPos, viewLeft, viewBot, xLow, yLow, scaleXY) : -1;
                    if (near >= 0)
                        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    if (!clicked || !inCanvas) return false;
                    selectedIdx = near;
                    return near >= 0;

                case PlacementState.Armed:
                    if (!clicked || !inCanvas) return false;
                    draftStart     = mouseProbeCoord;
                    draftEnd       = mouseProbeCoord;
                    draftStartScreen = mouseScreenPos;
                    placement        = PlacementState.Dragging;
                    return true;

                case PlacementState.Dragging:
                    if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left)) return false;
                    if ((mouseScreenPos - draftStartScreen).LengthSquared() >= 16f)
                        LockDraft();
                    else
                        placement = PlacementState.ClickMode;
                    return false;

                case PlacementState.ClickMode:
                    if (!clicked || !inCanvas) return false;
                    LockDraft();
                    return true;
            }
            return false;
        }

        int Nearest(Vector2 mouseScreenPos, float viewLeft, float viewBot, float xLow, float yLow, float scaleXY)
        {
            int nearest = -1;
            float nearestDist = HoverDist;
            for (int i = 0; i < locked.Count; i++)
            {
                var s = ToScreen(locked[i].Start, viewLeft, viewBot, xLow, yLow, scaleXY);
                var e = ToScreen(locked[i].End, viewLeft, viewBot, xLow, yLow, scaleXY);
                var dist = PointToSegmentDist(mouseScreenPos, s, e);
                if (dist <= nearestDist) { nearest = i; nearestDist = dist; }
            }
            return nearest;
        }

        public void Draw(ImDrawListPtr dl, float viewLeft, float viewBot,
            float xLow, float yLow, float scaleXY)
        {
            for (int i = 0; i < locked.Count; i++)
                DrawSingleRuler(dl, locked[i].Start, locked[i].End,
                    viewLeft, viewBot, xLow, yLow, scaleXY, i == selectedIdx);

            if (placement is PlacementState.Dragging or PlacementState.ClickMode)
                DrawSingleRuler(dl, draftStart, draftEnd,
                    viewLeft, viewBot, xLow, yLow, scaleXY, false);
        }

        void LockDraft()
        {
            placement = PlacementState.None;
            if ((draftEnd - draftStart).LengthSquared() > 0f)
            {
                locked.Add(new RulerLine(draftStart, draftEnd));
                selectedIdx = locked.Count - 1;
            }
        }

        void DrawSingleRuler(ImDrawListPtr dl, Vector2 start, Vector2 end,
            float viewLeft, float viewBot, float xLow, float yLow, float scaleXY, bool isSelected)
        {
            var s = ToScreen(start, viewLeft, viewBot, xLow, yLow, scaleXY);
            var e = ToScreen(end,   viewLeft, viewBot, xLow, yLow, scaleXY);

            uint lineCol = isSelected ? ColorSelected : ColorLine;
            dl.AddLine(s, e, lineCol, LineWidth);

            var (major, _) = ImGuiProbeSelector.TickIntervals(scaleXY, withMinor: false);
            var dir = e - s;
            float lenPx = dir.Length();

            if (lenPx >= 0.5f)
            {
                var dirUnit = dir / lenPx;
                // For an upward ruler (screen dir (0,-1)): perp = (1,0) = screen right.
                var perp = new Vector2(-dirUnit.Y, dirUnit.X);

                float minorSpacingPx = major * scaleXY / MinorTicksPerMajor;
                for (int k = 0; k * minorSpacingPx < lenPx - 0.5f; k++)
                {
                    bool  isMajor = k % MinorTicksPerMajor == 0;
                    float halfLen = isMajor ? MajorTickHalfLen : MinorTickHalfLen;
                    var   center  = s + dirUnit * (k * minorSpacingPx);
                    dl.AddLine(center - perp * halfLen, center + perp * halfLen, lineCol, LineWidth);

                    if (isMajor)
                    {
                        var label = ImGuiProbeSelector.FormatInterval(k / MinorTicksPerMajor * major, major);
                        DrawTickLabel(dl, center, label, perp, halfLen);
                    }
                }

                dl.AddLine(e - perp * MajorTickHalfLen, e + perp * MajorTickHalfLen, lineCol, LineWidth);
            }

            float dX  = end.X - start.X;
            float dY  = end.Y - start.Y;
            float r   = (float)Math.Sqrt(dX * dX + dY * dY);
            float deg = (float)(Math.Atan2(dY, dX) * 180.0 / Math.PI);

            string line1 = $"x: {ImGuiProbeSelector.FormatInterval(dX, major)} {Units}";
            string line2 = $"y: {ImGuiProbeSelector.FormatInterval(dY, major)} {Units}";
            string line3 = $"r: {ImGuiProbeSelector.FormatInterval(r, major)} {Units}";
            string line4 = $"deg: {deg:0}";

            float lineH = ImGui.CalcTextSize(line1).Y;
            float lblW  = Math.Max(Math.Max(Math.Max(
                ImGui.CalcTextSize(line1).X,
                ImGui.CalcTextSize(line2).X),
                ImGui.CalcTextSize(line3).X),
                ImGui.CalcTextSize(line4).X);
            float lblH = lineH * 4f;
            float lx   = e.X + 8f;
            float ly   = e.Y - lblH / 2f;

            dl.AddRectFilled(
                new Vector2(lx - LabelPad, ly - LabelPad),
                new Vector2(lx + lblW + LabelPad, ly + lblH + LabelPad),
                ColorLabelBg, 3f);
            dl.AddText(new Vector2(lx, ly),              ColorLabel, line1);
            dl.AddText(new Vector2(lx, ly + lineH),      ColorLabel, line2);
            dl.AddText(new Vector2(lx, ly + lineH * 2f), ColorLabel, line3);
            dl.AddText(new Vector2(lx, ly + lineH * 3f), ColorLabel, line4);
        }

        // Offset is computed so the text clears the tick end by 2 px regardless of ruler angle.
        static void DrawTickLabel(ImDrawListPtr dl, Vector2 tickCenter, string text,
            Vector2 perp, float halfLen)
        {
            var   tsz      = ImGui.CalcTextSize(text);
            var   leftDir  = new Vector2(-perp.X, -perp.Y);
            float halfExt  = (tsz.X / 2f) * Math.Abs(leftDir.X)
                           + (tsz.Y / 2f) * Math.Abs(leftDir.Y);
            var   center   = tickCenter + leftDir * (halfLen + 2f + halfExt);
            var   textPos  = center - new Vector2(tsz.X / 2f, tsz.Y / 2f);
            const float pad = 1f;
            dl.AddRectFilled(textPos - new Vector2(pad, pad),
                             textPos + tsz + new Vector2(pad, pad), ImGuiPalette.WithAlpha(ImGuiPalette.Black, 0x88), 2f);
            dl.AddText(textPos, ColorTickLabel, text);
        }

        static float PointToSegmentDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float lenSq = ab.X * ab.X + ab.Y * ab.Y;
            if (lenSq < 0.0001f) return (p - a).Length();
            float t = Math.Max(0f, Math.Min(1f,
                ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lenSq));
            return new Vector2(p.X - (a.X + t * ab.X), p.Y - (a.Y + t * ab.Y)).Length();
        }

        static Vector2 ToScreen(Vector2 point, float viewLeft, float viewBot,
            float xLow, float yLow, float scaleXY)
        {
            return new Vector2(
                viewLeft + (point.X - xLow) * scaleXY,
                viewBot  - (point.Y - yLow) * scaleXY);
        }
    }
}
