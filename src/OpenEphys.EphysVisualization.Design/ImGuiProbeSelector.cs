using Hexa.NET.ImGui;
using OpenEphys.ProbeInterface.NET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Represents one entry in an <see cref="ImGuiProbeSelector"/>'s legend: a swatch and what it means.
    /// </summary>
    /// <param name="Color">The swatch's color, in packed <c>0xAABBGGRR</c> format.</param>
    /// <param name="Label">What the color means.</param>
    /// <param name="DottedOutline">Whether the swatch is outlined with dots, as blocked contacts are.</param>
    /// <param name="OutlineOnly">Whether the swatch is drawn as an outline only, as a selection is.</param>
    public readonly record struct LegendEntry(uint Color, string Label, bool DottedOutline = false, bool OutlineOnly = false);

    /// <summary>
    /// Specifies what a drag across an <see cref="ImGuiProbeSelector"/>'s contacts does.
    /// </summary>
    public enum DragSelectIntent
    {
        /// <summary>
        /// No drag is in progress.
        /// </summary>
        Idle,

        /// <summary>
        /// The drag adds the contacts it sweeps to the selection.
        /// </summary>
        Add,

        /// <summary>
        /// The drag removes the contacts it sweeps from the selection, as when Ctrl+Shift is held.
        /// </summary>
        Remove,
    }

    /// <summary>
    /// Represents where one contact of an <see cref="ImGuiProbeSelector"/> stands in its selection.
    /// </summary>
    /// <param name="Selected">
    /// Whether the contact is selected: the selection an owner acts on. It accumulates across gestures (Shift+drag
    /// adds, Ctrl+Shift+drag removes) and leaves out blocked contacts while
    /// <see cref="ImGuiProbeSelector.SelectionSkipsBlocked"/> is set.
    /// </param>
    /// <param name="Inspected">
    /// Whether the contact was swept up by the same gestures, blocked or not, for showing what was swept without
    /// letting blocked contacts be acted on. Always set where <paramref name="Selected"/> is.
    /// </param>
    /// <param name="InDragBox">
    /// Whether the contact lies inside the current drag box, or under the last plain click, whatever the drag's
    /// intent. Unlike the selection, this does not accumulate across gestures.
    /// </param>
    public readonly record struct ContactState(bool Selected, bool Inspected, bool InDragBox);

    /// <summary>
    /// Provides an ImGui view of a probe: a minimap of the whole probe, and a zoomable view of part of it in
    /// which contacts can be selected by dragging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The owner decides what the contacts mean through <see cref="FillColor"/> and <see cref="Blocked"/>, so the
    /// view itself is the same for any probe.
    /// </para>
    /// <para>
    /// <see cref="DrawMinimap"/> and <see cref="DrawZoomedView"/> each fill the child window they are called in,
    /// and answer the wheel while it is over them: scroll, Shift to pan across, Ctrl to zoom. Size the minimap's
    /// child with <see cref="MinimapWidth"/>.
    /// </para>
    /// </remarks>
    public class ImGuiProbeSelector
    {
        readonly float? initialZoomWindow;
        readonly float initialZoomWindowFraction;
        readonly float? initialScrollX;
        readonly float? initialScrollY;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImGuiProbeSelector"/> class.
        /// </summary>
        /// <remarks>
        /// The view is placed on each probe as <see cref="Refresh"/> loads it.
        /// </remarks>
        /// <param name="zoomWindow">
        /// The zoomed view's major extent, in the units the probe file is written in, or null to take it from
        /// <paramref name="zoomWindowFraction"/>.
        /// </param>
        /// <param name="zoomWindowFraction">
        /// The zoomed view's major extent as a fraction of the probe's larger extent, used where
        /// <paramref name="zoomWindow"/> is null. One shows the whole probe.
        /// </param>
        /// <param name="scrollX">
        /// The horizontal position of the zoomed view's center on the probe, or null for the probe's center.
        /// </param>
        /// <param name="scrollY">
        /// The vertical position of the zoomed view's center on the probe, or null for the probe's center.
        /// </param>
        public ImGuiProbeSelector(float? zoomWindow = null, float zoomWindowFraction = 0.1f, float? scrollX = null,
            float? scrollY = null)
        {
            initialZoomWindow = zoomWindow;
            initialZoomWindowFraction = zoomWindowFraction;
            initialScrollX = scrollX;
            initialScrollY = scrollY;
            Contacts = new ContactStates(this);
        }

        /// <summary>
        /// Gets or sets the function giving the fill color, in packed <c>0xAABBGGRR</c> format, of the contact at
        /// an index into the probe's contacts.
        /// </summary>
        public Func<int, uint> FillColor { get; set; } = _ => ColContactDefault;

        /// <summary>
        /// Gets or sets the function saying whether the contact at an index into the probe's contacts is blocked
        /// from selection.
        /// </summary>
        public Func<int, bool> Blocked { get; set; } = _ => false;

        /// <summary>
        /// Gets or sets a value specifying whether blocked contacts are filled with <see cref="FillColor"/> rather
        /// than <see cref="BlockedFillColor"/>.
        /// </summary>
        public bool FillColorOverridesBlocked { get; set; } = false;

        bool[] inDragBox = Array.Empty<bool>();
        bool[] selected = Array.Empty<bool>();
        bool[] inspected = Array.Empty<bool>();

        /// <summary>
        /// Gets where each of the probe's contacts stands in the selection, in the probe's contact order.
        /// </summary>
        public IReadOnlyList<ContactState> Contacts { get; }

        sealed class ContactStates : IReadOnlyList<ContactState>
        {
            readonly ImGuiProbeSelector owner;

            public ContactStates(ImGuiProbeSelector owner) => this.owner = owner;

            public ContactState this[int index] =>
                new(owner.selected[index], owner.inspected[index], owner.inDragBox[index]);

            public int Count => owner.selected.Length;

            public IEnumerator<ContactState> GetEnumerator()
            {
                for (int i = 0; i < Count; i++)
                    yield return this[i];
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>
        /// Gets what the current drag does, or <see cref="DragSelectIntent.Idle"/> when no drag is in progress.
        /// </summary>
        public DragSelectIntent DragIntent { get; private set; } = DragSelectIntent.Idle;

        /// <summary>
        /// Gets the index into the probe's contacts of the contact under the pointer in the zoomed view, or -1
        /// when there is none.
        /// </summary>
        public int HoveredContactIndex { get; private set; } = -1;

        /// <summary>
        /// Gets or sets a value specifying whether blocked contacts are left out of the selection. Clear it to
        /// let a drag sweep them up, as selecting whole banks does.
        /// </summary>
        public bool SelectionSkipsBlocked { get; set; } = true;

        /// <summary>
        /// Gets or sets a value specifying whether the zoomed view draws grid lines and their axis labels.
        /// </summary>
        /// <remarks>
        /// The labels take a fixed pixel margin on each side, which in a narrow pane leaves little room for the
        /// probe itself, so a narrow view does better without them.
        /// </remarks>
        public bool ShowGrid { get; set; } = true;

        /// <summary>
        /// Gets or sets a value specifying whether the zoomed view shows the coordinates under the pointer.
        /// </summary>
        public bool ShowCoordinateReadout { get; set; } = true;

        /// <summary>
        /// Gets a value indicating whether the pointer was over the zoomed view when it was last drawn, for an owner
        /// whose keys, like the view's own, should work only there.
        /// </summary>
        public bool Hovered { get; private set; }

        /// <summary>
        /// Gets or sets the legend drawn in the zoomed view.
        /// </summary>
        public IReadOnlyList<LegendEntry> Legend { get; set; } = Array.Empty<LegendEntry>();

        /// <summary>
        /// Gets the color of the outline drawn around selected contacts, for a <see cref="Legend"/> entry.
        /// </summary>
        public static uint SelectionBorderColor => ColContactSelBorder;

        /// <summary>
        /// Gets the color of the outline drawn around contacts inspected but not selected, for a
        /// <see cref="Legend"/> entry.
        /// </summary>
        public static uint InspectionBorderColor => ColContactInspectBorder;

        /// <summary>
        /// Gets the color blocked contacts are filled with, unless <see cref="FillColorOverridesBlocked"/> is set,
        /// for a <see cref="Legend"/> entry.
        /// </summary>
        public static uint BlockedFillColor => ColContactBlocked;

        /// <summary>
        /// Occurs when a gesture has changed the selection.
        /// </summary>
        public event EventHandler? SelectionChanged;

        /// <summary>
        /// Pixel width reserved for the depth-ruler column on the left of the minimap and on
        /// both sides of the zoomed view.
        /// </summary>
        const float RulerColumnWidthPx = 68f;

        /// <summary>
        /// Pixel padding applied above and below the contact area inside the minimap panel.
        /// </summary>
        const float MinimapPaddingPx = 8f;

        // NB: spans enough decades to serve a file written in millimetres as well as one in
        // micrometres. The pixel thresholds pick the interval; the unit never enters into it.
        static readonly float[] TickCandidates =
        {
            0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.5f, 1f, 2f, 5f, 10f, 20f, 50f,
            100f, 200f, 500f, 1000f, 2000f, 5000f, 10000f, 20000f, 50000f, 100000f
        };

        const float MajorTickSpacingPx = 60f;
        const float MinorTickSpacingPx = 8f;

        /// <summary>
        /// A contact's index, position and extent in microns, as this component draws it.
        /// </summary>
        readonly record struct ProbeContact(int Index, Vector2 Position, Vector2 Size);

        // probe state
        ProbeGroup? probeGroup;
        IReadOnlyList<ProbeContact> contacts = Array.Empty<ProbeContact>();

        // probe bounds in microns
        float probeXMin, probeXMax, probeYMin, probeYMax;

        // scroll / zoom
        float scrollY, scrollX; // Defaulted to half probe width
        float zoomWindow; // defines the major axis length of the zoom window.
        float effectiveXWindow;

        // drag-select state machine
        readonly DragBox selectBox = new(ImGuiMouseButton.Left);
        bool dragHadShift, dragHadCtrl;
        bool[] preDragSelection = Array.Empty<bool>();
        bool[] preDragInspected = Array.Empty<bool>();

        readonly DragBox zoomBox = new(ImGuiMouseButton.Right);

        // middle-mouse pan
        bool isMiddlePanning;
        Vector2 middlePanLastPos;

        // ruler tool
        // NB: si_units is a label, not a scale. The coordinates in the file are self consistent
        // and are drawn normalized against the probe bounds, so the unit is only ever used to say
        // what the tick and readout numbers mean.
        string units = "um";

        readonly ImGuiProbeRuler ruler = new();

        /// <summary>
        /// Every key the probe view answers: R arms a ruler, which the next drag or pair of clicks places, Delete
        /// removes the selected ruler and Shift+Delete all of them, and Escape cancels a zoom box or a ruler being
        /// placed, then deselects a ruler, then clears the selection.
        /// </summary>
        public IEnumerable<ImGuiKey> Hotkeys => new[] { ImGuiKey.R, ImGuiKey.Delete, ImGuiKey.Escape };

        int lastDrawnFrame = -1;

        // visual constants
        static readonly uint  ColContactDefault   = ImGuiPalette.Grey0x4D;
        static readonly uint  ColContactBlocked   = ImGuiPalette.Black;
        static readonly uint  ColBlockedOutline   = ImGuiPalette.Grey0x77;
        static readonly uint  ColContactSelBorder = ImGuiPalette.Yellow;
        static readonly uint  ColContactInspectBorder = ImGuiPalette.Grey0x99;
        const float ContactSelBorderWidth = 2f;
        static readonly uint  ColContour          = ImGuiPalette.Grey0x80;
        const float ContourWidthMinimap = 1f;
        const float ContourWidthZoomed  = 2f;
        static readonly uint ColZoomBoxFill = ImGuiPalette.WithAlpha(ImGuiPalette.Yellow, 0x22);
        static readonly uint  ColZoomBoxBorder    = ImGuiPalette.Yellow;
        static readonly uint  ColRulerAxis        = ImGuiPalette.Grey0x55;
        static readonly uint  ColRulerMajorTick   = ImGuiPalette.Grey0x88;
        static readonly uint  ColRulerLabel       = ImGuiPalette.Grey0x99;
        static readonly uint  ColGridMajor        = ImGuiPalette.Grey0x55;
        static readonly uint  ColGridMinor        = ImGuiPalette.Grey0x30;
        const float RulerMajorTickLen   = 6f;
        const float RulerMinorTickLen   = 3f;
        static readonly uint  ColDragSelect       = ImGuiPalette.Yellow;
        static readonly uint  ColDragDeselect     = ImGuiPalette.Red;
        static readonly uint ColCoordOverlayBg = ImGuiPalette.WithAlpha(ImGuiPalette.Black, 0xBB);
        static readonly uint  ColBottomInfoText   = ImGuiPalette.White;
        static readonly uint  ColCrosshair        = ImGuiPalette.WithAlpha(ImGuiPalette.White, 0xC0);
        const float GridMarginHPx       = 60f;
        const float GridMarginVPx       = 60f;
        static readonly uint ColContourFill = ImGuiPalette.WithAlpha(ImGuiPalette.Black, 0xBB);

        /// <summary>
        /// Projects probeinterface contacts onto the position and extent this component draws with,
        /// resolving each contact's extent from whichever shape parameters its
        /// <see cref="ContactShape"/> uses.
        /// </summary>
        static IReadOnlyList<ProbeContact> CreateContacts(IReadOnlyList<Contact> contacts)
        {
            var result = new ProbeContact[contacts.Count];
            for (int i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i];
                var sp = c.ShapeParams;
                var size = c.Shape switch
                {
                    ContactShape.Circle => new Vector2((float)(sp.Radius!.Value * 2), (float)(sp.Radius!.Value * 2)),
                    ContactShape.Rect => new Vector2((float)sp.Width!.Value, (float)sp.Height!.Value),
                    _ => new Vector2((float)sp.Width!.Value, (float)sp.Width!.Value)
                };
                result[i] = new ProbeContact(i, new Vector2((float)c.PosX, (float)c.PosY), size);
            }
            return result;
        }

        /// <summary>
        /// Shows a probe, clearing the selection and placing the view as the constructor was told.
        /// </summary>
        /// <remarks>
        /// <see cref="Contacts"/> follows the probe's contact order from here on.
        /// </remarks>
        /// <param name="probeGroup">The probe group to show, or null to show none.</param>
        public void Refresh(SingleProbeGroup? probeGroup)
        {
            var contacts = probeGroup is null
                ? Array.Empty<ProbeContact>()
                : CreateContacts(probeGroup.Probe.Contacts);
            this.probeGroup = probeGroup;
            this.contacts = contacts;
            selected = new bool[contacts.Count];
            inDragBox  = new bool[contacts.Count];
            inspected = new bool[contacts.Count];
            preDragSelection = new bool[contacts.Count];
            preDragInspected = new bool[contacts.Count];
            units = probeGroup?.Probes.FirstOrDefault()?.SiUnits.ToString() ?? "um";
            ruler.Units = units;

            probeXMin = probeXMax = probeYMin = probeYMax = 0f;
            bool init = false;
            void Expand(float x, float y)
            {
                if (!init) { probeXMin = probeXMax = x; probeYMin = probeYMax = y; init = true; return; }
                if (x < probeXMin) probeXMin = x; if (x > probeXMax) probeXMax = x;
                if (y < probeYMin) probeYMin = y; if (y > probeYMax) probeYMax = y;
            }

            // Expand by full contact extents so edge contacts are not clipped.
            foreach (var c in contacts)
            {
                Expand(c.Position.X - c.Size.X / 2f, c.Position.Y - c.Size.Y / 2f);
                Expand(c.Position.X + c.Size.X / 2f, c.Position.Y + c.Size.Y / 2f);
            }

            if (probeGroup != null)
            {
                foreach (var probe in probeGroup.Probes)
                {
                    if (probe?.ProbePlanarContour == null) continue;
                    foreach (var v in probe.ProbePlanarContour) Expand((float)v[0], (float)v[1]);
                }
            }

            var probeExtent = Math.Max(probeXMax - probeXMin, probeYMax - probeYMin);
            zoomWindow = initialZoomWindow
                ?? (probeExtent > 0 ? initialZoomWindowFraction * probeExtent : 1f);
            scrollY = initialScrollY ?? (probeYMin + probeYMax) / 2f;
            ClampScroll();
            scrollX = initialScrollX ?? (probeXMin + probeXMax) / 2f;
            var (mxMin, mxMax, _, _) = GetMmBounds();
            effectiveXWindow = mxMax - mxMin;
        }

        /// <summary>
        /// Returns the width in pixels of a child window that fits the minimap, its depth ruler included, at
        /// <paramref name="height"/>.
        /// </summary>
        /// <param name="height">The height of the minimap's child window, in pixels.</param>
        /// <param name="availableWidth">
        /// The width the minimap shares with whatever is beside it, in pixels, of which it takes no more than a
        /// quarter so that it does not crowd out a wide window.
        /// </param>
        public float MinimapWidth(float height, float availableWidth)
        {
            if (contacts.Count == 0) return RulerColumnWidthPx + 24f;
            float probeH = probeYMax - probeYMin;
            float probeW = probeXMax - probeXMin;
            float mmH = height - MinimapPaddingPx * 2f;
            float scale = mmH > 0 ? mmH / probeH : 0.01f;
            float contactW = Math.Max(24f, probeW * scale + 8f);
            return RulerColumnWidthPx + Math.Min(contactW * 1.33f, availableWidth * 0.24f);
        }

        // NB: the minimap draws its zoom box from this, and is drawn before the zoomed view, so after a resize the
        // box takes the zoomed view's new shape a frame late.
        void UpdateLayout(float height, float width)
        {
            var (mxMin, mxMax, _, _) = GetMmBounds();
            float mmTotalW = mxMax - mxMin;
            effectiveXWindow = height > 0 && width > 0
                ? Math.Min(mmTotalW, zoomWindow * width / height)
                : mmTotalW;
            ClampScrollX();
        }

        /// <summary>
        /// Deselects all contacts and clears drag state. Does not fire <see cref="SelectionChanged"/>.
        /// </summary>
        public void ClearSelection()
        {
            Array.Clear(selected,  0, selected.Length);
            Array.Clear(inDragBox,   0, inDragBox.Length);
            Array.Clear(inspected, 0, inspected.Length);
            DragIntent = DragSelectIntent.Idle;
        }

        /// <summary>
        /// Draws the minimap of the whole probe, filling the current child window, with a box showing what the
        /// zoomed view shows. Dragging in it moves the zoomed view. Call once per frame.
        /// </summary>
        public void DrawMinimap()
        {
            if (contacts.Count == 0) return;

            float availH = ImGui.GetContentRegionAvail().Y;
            if (ImGui.IsWindowHovered())
                HandleWheel();

            var (mmXMin, mmXMax, mmYMin, mmYMax) = GetMmBounds();
            float mmTotalW = mmXMax - mmXMin;
            float mmTotalH = mmYMax - mmYMin;
            float mmH  = availH - MinimapPaddingPx * 2f;
            float scale = mmH > 0 ? mmH / mmTotalH : 0.01f;
            float rendW = mmTotalW * scale;

            var dl = ImGui.GetWindowDrawList();
            var cp = ImGui.GetCursorScreenPos();
            float viewTop     = cp.Y + MinimapPaddingPx;
            float viewBot     = viewTop + mmH;
            float contactLeft = cp.X + RulerColumnWidthPx;

            // probeYMin/Max already include contact half-extents, so no adjustment needed here.
            float rulerTop = viewTop + (mmYMax - probeYMax) * scale;
            float rulerBot = viewTop + (mmYMax - probeYMin) * scale;
            DrawDepthRuler(dl, contactLeft - 2f, rulerTop, rulerBot, probeYMin, probeYMax, units);

            if (probeGroup != null)
                foreach (var probe in probeGroup.Probes)
                    DrawContour(dl, probe, contactLeft, viewTop, mmXMin, mmYMax, scale, ContourWidthMinimap);

            for (int i = 0; i < contacts.Count; i++)
            {
                var c  = contacts[i];
                float ew = Math.Max(1f, c.Size.X * scale);
                float eh = Math.Max(1f, c.Size.Y * scale);
                float sx = contactLeft + (c.Position.X - mmXMin) * scale - ew / 2f;
                float sy = viewTop     + (mmYMax - c.Position.Y) * scale - eh / 2f;
                dl.AddRectFilled(new Vector2(sx, sy), new Vector2(sx + ew, sy + eh), FillColor(c.Index));
            }

            float yHalf    = zoomWindow / 2f;
            float xHalf    = effectiveXWindow / 2f;
            float boxTop   = Clamp(viewTop + (mmYMax - (scrollY + yHalf)) * scale, viewTop, viewBot);
            float boxBot   = Clamp(viewTop + (mmYMax - (scrollY - yHalf)) * scale, viewTop, viewBot);
            float boxLeft  = Clamp(contactLeft + (scrollX - xHalf - mmXMin) * scale, contactLeft, contactLeft + rendW);
            float boxRight = Clamp(contactLeft + (scrollX + xHalf - mmXMin) * scale, contactLeft, contactLeft + rendW);
            PixelRect.Fill(dl, new Vector2(boxLeft, boxTop), new Vector2(boxRight, boxBot), ColZoomBoxFill);
            PixelRect.Outline(dl, new Vector2(boxLeft, boxTop), new Vector2(boxRight, boxBot), ColZoomBoxBorder);

            ImGui.SetCursorScreenPos(new Vector2(contactLeft, viewTop));
            ImGui.InvisibleButton("##minimapbtn", new Vector2(Math.Max(1f, rendW), Math.Max(1f, mmH)));
            if (ImGui.IsItemActive())
            {
                var mmp = ImGui.GetMousePos();
                scrollY = mmYMax - (mmp.Y - viewTop)     / mmH   * mmTotalH;
                scrollX = mmXMin + (mmp.X - contactLeft) / rendW * mmTotalW;
                ClampScroll(); ClampScrollX();
            }

            ImGui.SetCursorScreenPos(cp);
            ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, availH));
        }

        /// <summary>
        /// Draws the zoomed view of part of the probe, filling the current child window. Call once per frame.
        /// </summary>
        /// <param name="selectionEnabled">
        /// Whether contacts can be selected by dragging and clicking, as they cannot where nothing could be done
        /// with a selection.
        /// </param>
        public void DrawZoomedView(bool selectionEnabled)
        {
            Hovered = ImGui.IsWindowHovered();

            // NB: a view that wasn't drawn last frame was hidden, such as behind another tab. A ruler armed or half
            // placed there is dropped, since the user has moved on from it.
            var frame = ImGui.GetFrameCount();
            if (frame != lastDrawnFrame + 1)
                ruler.Cancel();
            lastDrawnFrame = frame;

            if (contacts.Count == 0) { ImGui.TextDisabled("No contacts."); return; }

            HoveredContactIndex = -1;

            var region = ImGui.GetContentRegionAvail();
            UpdateLayout(region.Y, region.X);
            if (ImGui.IsWindowHovered())
                HandleWheel();

            float xLow = scrollX - effectiveXWindow / 2f;
            float yLow = scrollY - zoomWindow       / 2f;

            var avail  = ImGui.GetContentRegionAvail();
            float zW   = avail.X;
            float scaleXY = effectiveXWindow > 0 && zoomWindow > 0
                ? Math.Min(zW / effectiveXWindow, avail.Y / zoomWindow) : 1f;
            float drawW = effectiveXWindow * scaleXY;
            float drawH = zoomWindow       * scaleXY;

            var dl = ImGui.GetWindowDrawList();
            var cp = ImGui.GetCursorScreenPos();
            float viewLeft = cp.X + (zW - drawW) / 2f;
            float viewTop  = cp.Y + (avail.Y - drawH) / 2f;
            float viewBot  = viewTop + drawH;

            float marginH = ShowGrid ? GridMarginHPx : 0f;
            float marginV = ShowGrid ? GridMarginVPx : 0f;
            float gridLeft  = cp.X + marginH;
            float gridRight = cp.X + avail.X - marginH;
            float gridTop   = cp.Y + marginV;
            float gridBot   = cp.Y + avail.Y - marginV;

            // NB: lines and labels are drawn in separate passes but are one grid, so the interval is
            // chosen once here rather than derived twice from scaleXY and relied on to agree.
            var (gridMajor, gridMinor) = TickIntervals(scaleXY, ShowGrid);

            if (ShowGrid)
            {
                DrawGridLines(dl, gridLeft, gridRight, gridTop, gridBot,
                    viewLeft, viewBot, xLow, yLow, scaleXY, gridMajor, gridMinor);
            }

            ImGui.PushClipRect(new Vector2(gridLeft, gridTop), new Vector2(gridRight, gridBot), true);

            if (probeGroup != null)
                foreach (var probe in probeGroup.Probes)
                {
                    DrawContourFill(dl, probe, viewLeft, viewBot, xLow, yLow, scaleXY);
                    DrawContour(dl, probe, viewLeft, viewBot, xLow, -1f /* unused */, scaleXY,
                        ContourWidthZoomed, yOffset: yLow, yUp: true);
                }

            for (int i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i];
                if (c.Position.Y + c.Size.Y / 2f < yLow ||
                    c.Position.Y - c.Size.Y / 2f > yLow + zoomWindow) continue;
                if (c.Position.X + c.Size.X / 2f < xLow ||
                    c.Position.X - c.Size.X / 2f > xLow + effectiveXWindow) continue;

                float rendEW = Math.Max(2f, c.Size.X * scaleXY);
                float rendEH = Math.Max(2f, c.Size.Y * scaleXY);
                float sx = viewLeft + (c.Position.X - xLow) * scaleXY - rendEW / 2f;
                float sy = viewBot  - (c.Position.Y - yLow) * scaleXY - rendEH / 2f;
                var rMin = new Vector2(sx, sy);
                var rMax = new Vector2(sx + rendEW, sy + rendEH);

                bool blocked = Blocked(c.Index);
                dl.AddRectFilled(rMin, rMax, blocked && !FillColorOverridesBlocked
                    ? ColContactBlocked
                    : FillColor(c.Index));
                if (blocked && !FillColorOverridesBlocked)
                    DrawDottedRect(dl, rMin, rMax, ColBlockedOutline);

                if (selected[i])
                    dl.AddRect(new Vector2(sx - 1, sy - 1), new Vector2(sx + rendEW + 1, sy + rendEH + 1),
                        ColContactSelBorder, 0f, ContactSelBorderWidth);
                else if (inspected[i])
                    dl.AddRect(new Vector2(sx - 1, sy - 1), new Vector2(sx + rendEW + 1, sy + rendEH + 1),
                        ColContactInspectBorder, 0f, ContactSelBorderWidth);
            }

            var mp = ImGui.GetMousePos();
            float mousePx = xLow + (mp.X - viewLeft) / scaleXY;
            float mousePy = yLow + (viewBot - mp.Y)  / scaleXY;
            bool inCanvas = Hovered &&
                            mp.X >= gridLeft && mp.X <= gridRight &&
                            mp.Y >= gridTop  && mp.Y <= gridBot;
            var canvasMin = new Vector2(gridLeft, gridTop);
            var canvasMax = new Vector2(gridRight, gridBot);

            HandleKeys(selectionEnabled);
            var rulerTookClick = ruler.HandleInput(new Vector2(mousePx, mousePy), inCanvas,
                mp, viewLeft, viewBot, xLow, yLow, scaleXY);
            if (ruler.Placing && inCanvas)
                ImGui.SetMouseCursor(ImGuiMouseCursor.None);

            if (selectionEnabled)
                HandleDragSelect(dl, canvasMin, canvasMax, viewLeft, viewBot, xLow, yLow, scaleXY, rulerTookClick);
            HandleZoomBox(dl, canvasMin, canvasMax, inCanvas, avail, viewLeft, viewBot, xLow, yLow, scaleXY);

            if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
            {
                isMiddlePanning = true;
                middlePanLastPos = ImGui.GetMousePos();
            }
            if (isMiddlePanning)
            {
                if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
                {
                    scrollX -= (mp.X - middlePanLastPos.X) / scaleXY;
                    scrollY += (mp.Y - middlePanLastPos.Y) / scaleXY;
                    ClampScroll(); ClampScrollX();
                    middlePanLastPos = mp;
                }
                else isMiddlePanning = false;
            }

            ImGui.PopClipRect();

            if (ShowGrid)
            {
                DrawGridLabels(dl, cp.X, cp.X + avail.X, cp.Y, cp.Y + avail.Y,
                    gridLeft, gridRight, gridTop, gridBot, viewLeft, viewBot, xLow, yLow, scaleXY, units,
                    gridMajor);
            }

            if (inCanvas)
            {
                if (ShowCoordinateReadout)
                {
                    string coord = $"({mousePx:0.##} {units}, {mousePy:0.##} {units})";
                    var sz  = ImGui.CalcTextSize(coord);
                    const float pad = 5f;
                    float refBot  = cp.Y + avail.Y - 18f;
                    var bgMin = new Vector2(cp.X + avail.X - sz.X - pad * 3f, refBot - sz.Y - pad);
                    var bgMax = new Vector2(cp.X + avail.X - pad, refBot);
                    dl.AddText(new Vector2(bgMin.X + pad, bgMin.Y + pad), ColBottomInfoText, coord);
                }

                for (int i = 0; i < contacts.Count; i++)
                {
                    var c = contacts[i];
                    float hw = c.Size.X / 2f, hh = c.Size.Y / 2f;
                    if (mousePx >= c.Position.X - hw && mousePx <= c.Position.X + hw &&
                        mousePy >= c.Position.Y - hh && mousePy <= c.Position.Y + hh)
                    { HoveredContactIndex = i; break; }
                }
            }

            DrawLegend(dl, cp, avail);
            ruler.Draw(dl, viewLeft, viewBot, xLow, yLow, scaleXY);
            if (ruler.Placing && inCanvas)
            {
                dl.AddLine(new Vector2(mp.X, gridTop), new Vector2(mp.X, gridBot), ColCrosshair);
                dl.AddLine(new Vector2(gridLeft, mp.Y), new Vector2(gridRight, mp.Y), ColCrosshair);
            }

            ImGui.Dummy(avail);
        }

        /// <summary>
        /// Draws an Add button, which arms a ruler for the zoomed view as R does, and a Clear button, which removes
        /// every ruler. The owner draws them wherever suits it. Add stays lit while a ruler waits to be placed, and
        /// pressing it then cancels the ruler.
        /// </summary>
        public void DrawRulerControls()
        {
            var placing = ruler.Placing;
            if (placing)
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));

            if (ImGui.Button("Add##ruler"))
                ToggleRuler();
            ImGuiWidgets.Tooltip("Add a ruler to the probe view. Click and drag in the view, or click its two ends, " +
                "to place it. Click a ruler to select it, and press Delete to remove it.\nKey: R over the probe view");

            if (placing)
                ImGui.PopStyleColor();

            ImGui.SameLine();
            ImGui.BeginDisabled(ruler.Count == 0);
            if (ImGui.Button("Clear##rulers"))
                ruler.RemoveAll();
            ImGuiWidgets.Tooltip("Remove every ruler from the probe view.\nKey: Shift+Delete over the probe view",
                "There are no rulers.");
            ImGui.EndDisabled();
        }

        void ToggleRuler()
        {
            if (ruler.Placing) ruler.Cancel();
            else ruler.Arm();
        }

        void HandleKeys(bool selectionEnabled)
        {
            // NB: Escape backs out of the most recent thing first, so cancelling a ruler doesn't also lose the
            // selection.
            if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                if (zoomBox.Dragging)
                    zoomBox.Cancel();
                else if (ruler.Placing)
                    ruler.Cancel();
                else if (ruler.HasSelection)
                    ruler.Deselect();
                else if (selectionEnabled)
                    ClearOnEscape();
            }

            if (!Hovered || ImGui.GetIO().WantTextInput) return;

            if (ImGui.IsKeyPressed(ImGuiKey.R, false))
                ToggleRuler();

            if (ImGui.IsKeyPressed(ImGuiKey.Delete, false))
            {
                if (ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift))
                    ruler.RemoveAll();
                else
                    ruler.RemoveSelected();
            }
        }

        void HandleWheel()
        {
            var io = ImGui.GetIO();
            if (io.MouseWheel == 0f) return;

            bool ctrl  = ImGui.IsKeyDown(ImGuiKey.LeftCtrl)  || ImGui.IsKeyDown(ImGuiKey.RightCtrl);
            bool shift = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
            var (_, _, mmYMin, mmYMax) = GetMmBounds();

            if (ctrl)
            {
                float factor = io.MouseWheel > 0f ? 0.85f : 1.15f;
                zoomWindow = Math.Max(50f, Math.Min(mmYMax - mmYMin, zoomWindow * factor));
                ClampScroll(); ClampScrollX();
            }
            else if (shift)
            {
                scrollX -= io.MouseWheel * effectiveXWindow * 0.1f;
                ClampScrollX();
            }
            else
            {
                scrollY += io.MouseWheel * zoomWindow * 0.1f;
                ClampScroll();
            }
        }

        /// <summary>
        /// Right click and drag a box to zoom to it, or double-click to zoom all the way out.
        /// </summary>
        void HandleZoomBox(ImDrawListPtr dl, Vector2 canvasMin, Vector2 canvasMax, bool inCanvas, Vector2 region,
            float viewLeft, float viewBot, float xLow, float yLow, float scaleXY)
        {
            if (inCanvas && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Right))
            {
                zoomBox.Cancel();
                ZoomAll(region);
            }
            else if (inCanvas && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                zoomBox.Begin();

            if (zoomBox.Current(canvasMin, canvasMax) is { } box && (box.Wide || box.Tall))
            {
                PixelRect.Fill(dl, box.Min, box.Max, ImGuiWaveformPanel.ColZoomBoxFill);
                PixelRect.Outline(dl, box.Min, box.Max, ImGuiWaveformPanel.ColZoomBox);
            }

            if (!zoomBox.Released(canvasMin, canvasMax, out var end) || !end.Wide && !end.Tall)
                return;

            // NB: one scale serves both axes, so the zoom is set by whichever side of the box needs more room at the
            // view's shape.
            var (_, _, mmYMin, mmYMax) = GetMmBounds();
            var widthUm = (end.Max.X - end.Min.X) / scaleXY;
            var heightUm = (end.Max.Y - end.Min.Y) / scaleXY;
            zoomWindow = Math.Max(50f, Math.Min(mmYMax - mmYMin, Math.Max(heightUm, widthUm * region.Y / region.X)));
            scrollX = xLow + ((end.Min.X + end.Max.X) / 2f - viewLeft) / scaleXY;
            scrollY = yLow + (viewBot - (end.Min.Y + end.Max.Y) / 2f) / scaleXY;
            ClampScroll();
            UpdateLayout(region.Y, region.X);
        }

        // NB: the view's width follows from the zoom, so the layout is redone before the view is clamped across,
        // which would otherwise use the width from before. HandleZoomBox does the same.
        void ZoomAll(Vector2 region)
        {
            var (_, _, mmYMin, mmYMax) = GetMmBounds();
            zoomWindow = mmYMax - mmYMin;
            scrollX = (probeXMin + probeXMax) / 2f;
            scrollY = (probeYMin + probeYMax) / 2f;
            ClampScroll();
            UpdateLayout(region.Y, region.X);
        }

        #region Private: scroll clamping

        (float xMin, float xMax, float yMin, float yMax) GetMmBounds()
        {
            float pw = probeXMax - probeXMin;
            float ph = probeYMax - probeYMin;
            const float m = 0.1f;
            return (probeXMin - m * pw, probeXMax + m * pw,
                    probeYMin - m * ph, probeYMax + m * ph);
        }

        void ClampScroll()
        {
            var (_, _, lo, hi) = GetMmBounds();
            float half = zoomWindow / 2f;
            if (lo + half > hi - half) { scrollY = (lo + hi) / 2f; return; }
            scrollY = Clamp(scrollY, lo + half, hi - half);
        }

        void ClampScrollX()
        {
            var (lo, hi, _, _) = GetMmBounds();
            float half = effectiveXWindow / 2f;
            if (lo + half > hi - half) { scrollX = (lo + hi) / 2f; return; }
            scrollX = Clamp(scrollX, lo + half, hi - half);
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        #endregion

        #region Private: drag-select state machine

        void HandleDragSelect(ImDrawListPtr dl, Vector2 canvasMin, Vector2 canvasMax, float viewLeft, float viewBot,
            float xLow, float yLow, float scaleXY, bool clickTaken)
        {
            if (!ImGui.IsWindowHovered()) return;

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !selectBox.Dragging && !clickTaken)
            {
                selectBox.Begin();
                dragHadShift = ImGui.IsKeyDown(ImGuiKey.LeftShift)  || ImGui.IsKeyDown(ImGuiKey.RightShift);
                dragHadCtrl  = ImGui.IsKeyDown(ImGuiKey.LeftCtrl)   || ImGui.IsKeyDown(ImGuiKey.RightCtrl);
                Array.Copy(selected,  preDragSelection, selected.Length);
                Array.Copy(inspected, preDragInspected, inspected.Length);
                DragIntent = (dragHadShift && dragHadCtrl) ? DragSelectIntent.Remove : DragSelectIntent.Add;
            }

            if (selectBox.Current(canvasMin, canvasMax) is not { } box) return;

            bool deselect = dragHadShift && dragHadCtrl;
            bool additive = dragHadShift && !dragHadCtrl;
            if (box.Wide || box.Tall)
            {
                float minX = xLow + (box.Min.X - viewLeft) / scaleXY;
                float maxX = xLow + (box.Max.X - viewLeft) / scaleXY;
                float minY = yLow + (viewBot - box.Max.Y) / scaleXY;
                float maxY = yLow + (viewBot - box.Min.Y) / scaleXY;

                Array.Clear(inDragBox, 0, inDragBox.Length);
                UpdateBoxContacts(minX, maxX, minY, maxY, inDragBox, true, SelectionSkipsBlocked);

                AccumulateSelection(selected, preDragSelection, SelectionSkipsBlocked,
                    minX, maxX, minY, maxY, additive, deselect);
                AccumulateSelection(inspected, preDragInspected, false,
                    minX, maxX, minY, maxY, additive, deselect);

                PixelRect.Outline(dl, box.Min, box.Max, deselect ? ColDragDeselect : ColDragSelect);
            }

            if (selectBox.Released(canvasMin, canvasMax, out var end))
            {
                if (!end.Wide && !end.Tall)
                    ApplyClick(viewLeft, viewBot, xLow, yLow, scaleXY, additive, deselect);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        void ClearOnEscape()
        {
            Array.Clear(selected,  0, selected.Length);
            Array.Clear(inDragBox,   0, inDragBox.Length);
            Array.Clear(inspected, 0, inspected.Length);
            selectBox.Cancel();
            DragIntent = DragSelectIntent.Idle;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        void AccumulateSelection(bool[] target, bool[] preDrag, bool skipBlocked,
            float minX, float maxX, float minY, float maxY, bool additive, bool deselect)
        {
            if (!additive && !deselect)
                Array.Clear(target, 0, target.Length);
            else
                Array.Copy(preDrag, target, target.Length);
            UpdateBoxContacts(minX, maxX, minY, maxY, target, !deselect, skipBlocked);
        }

        void ApplyClick(float viewLeft, float viewBot, float xLow, float yLow,
            float scaleXY, bool additive, bool deselect)
        {
            // NB: where the button went down, which a click may drift from by less than the drag threshold.
            var at = ImGui.GetIO().MouseClickedPos[0];
            float cx = xLow + (at.X - viewLeft) / scaleXY;
            float cy = yLow + (viewBot - at.Y)  / scaleXY;

            Array.Clear(inDragBox, 0, inDragBox.Length);
            if (!additive && !deselect)
                Array.Clear(selected, 0, selected.Length);
            else
                Array.Copy(preDragSelection, selected, selected.Length);
            if (!additive && !deselect)
                Array.Clear(inspected, 0, inspected.Length);
            else
                Array.Copy(preDragInspected, inspected, inspected.Length);

            for (int i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i];
                float hw = c.Size.X / 2f, hh = c.Size.Y / 2f;
                if (cx < c.Position.X - hw || cx > c.Position.X + hw ||
                    cy < c.Position.Y - hh || cy > c.Position.Y + hh) continue;

                bool blocked = Blocked(contacts[i].Index);
                if (!(SelectionSkipsBlocked && blocked))
                {
                    selected[i] = !deselect;
                    inDragBox[i]  = true;
                }
                inspected[i] = !deselect;
            }
        }

        void UpdateBoxContacts(float minX, float maxX, float minY, float maxY,
            bool[] target, bool value, bool skipBlocked)
        {
            for (int i = 0; i < contacts.Count; i++)
            {
                if (skipBlocked && Blocked(contacts[i].Index)) continue;
                var c = contacts[i];
                if (c.Position.X + c.Size.X / 2f >= minX && c.Position.X - c.Size.X / 2f <= maxX &&
                    c.Position.Y + c.Size.Y / 2f >= minY && c.Position.Y - c.Size.Y / 2f <= maxY)
                    target[i] = value;
            }
        }

        #endregion

        #region Private: rendering helpers

        // Minimap path: origin=(contactLeft, viewTop), y-axis grows down, probe-y via (mmYMax - probeY).
        // Zoomed path:  origin=(viewLeft, viewBot),    y-axis grows up,   probe-y via (probeY - yOffset).
        static void DrawContour(ImDrawListPtr dl, Probe probe,
            float originX, float originY, float xRef, float yRef,
            float scale, float width,
            float yOffset = 0f, bool yUp = false)
        {
            if (probe?.ProbePlanarContour == null || probe.ProbePlanarContour.Length < 2) return;
            var pts = probe.ProbePlanarContour;
            for (int j = 0; j < pts.Length; j++)
            {
                var c0 = pts[j];
                var c1 = pts[(j + 1) % pts.Length];
                Vector2 p0, p1;
                if (yUp)
                {
                    p0 = new Vector2(originX + ((float)c0[0] - xRef) * scale, originY - ((float)c0[1] - yOffset) * scale);
                    p1 = new Vector2(originX + ((float)c1[0] - xRef) * scale, originY - ((float)c1[1] - yOffset) * scale);
                }
                else
                {
                    p0 = new Vector2(originX + ((float)c0[0] - xRef) * scale, originY + (yRef - (float)c0[1]) * scale);
                    p1 = new Vector2(originX + ((float)c1[0] - xRef) * scale, originY + (yRef - (float)c1[1]) * scale);
                }
                dl.AddLine(p0, p1, ColContour, width);
            }
        }

        static void DrawDepthRuler(ImDrawListPtr dl, float rightX, float viewTop, float viewBot,
            float yMin, float yMax, string units, float gridLeft = -1f, float gridRight = -1f,
            bool rightSide = false, float yLabelBase = 0f)
        {
            float pxH = viewBot - viewTop;
            float range = yMax - yMin;
            if (pxH <= 0 || range <= 0) return;

            float pxPerUnit = pxH / range;
            bool hasGrid = gridLeft >= 0f && gridRight > gridLeft;

            dl.AddLine(new Vector2(rightX, viewTop), new Vector2(rightX, viewBot), ColRulerAxis);

            var (major, minor) = TickIntervals(pxPerUnit, hasGrid);

            if (minor > 0f)
            {
                float first = (float)(Math.Ceiling((double)yMin / minor) * minor);
                for (float y = first; y <= yMax + 1f; y += minor)
                {
                    float sy = viewBot - (y - yMin) * pxPerUnit;
                    if (rightSide) dl.AddLine(new Vector2(rightX, sy), new Vector2(rightX + RulerMinorTickLen, sy), ColRulerAxis);
                    else           dl.AddLine(new Vector2(rightX - RulerMinorTickLen, sy), new Vector2(rightX, sy), ColRulerAxis);
                    if (hasGrid) DrawDottedHLine(dl, gridLeft, gridRight, sy, ColGridMinor);
                }
            }

            float firstMaj = (float)(Math.Ceiling((double)yMin / major) * major);
            for (float y = firstMaj; y <= yMax + 1f; y += major)
            {
                float sy = viewBot - (y - yMin) * pxPerUnit;
                if (rightSide) dl.AddLine(new Vector2(rightX, sy), new Vector2(rightX + RulerMajorTickLen, sy), ColRulerMajorTick);
                else           dl.AddLine(new Vector2(rightX - RulerMajorTickLen, sy), new Vector2(rightX, sy), ColRulerMajorTick);
                if (hasGrid)
                    dl.AddLine(new Vector2(gridLeft, sy), new Vector2(gridRight, sy), ColGridMajor);
                string lbl = FormatInterval(y - yLabelBase, major, units);
                var tsz = ImGui.CalcTextSize(lbl);
                float lblX = rightSide ? rightX + 8f : rightX - 8f - tsz.X;
                dl.AddText(new Vector2(lblX, sy - tsz.Y / 2f), ColRulerLabel, lbl);
            }
        }

        static void DrawGridLines(ImDrawListPtr dl,
            float lineLeft, float lineRight, float lineTop, float lineBot,
            float viewLeft, float viewBot, float xLow, float yLow, float scaleXY,
            float major, float minor)
        {
            if (scaleXY <= 0) return;

            // Y (horizontal) grid lines
            float yMinV = yLow + (viewBot - lineBot) / scaleXY;
            float yMaxV = yLow + (viewBot - lineTop) / scaleXY;

            if (minor > 0f)
            {
                float first = (float)(Math.Ceiling((double)yMinV / minor) * minor);
                for (float y = first; y <= yMaxV + 1f; y += minor)
                {
                    float sy = viewBot - (y - yLow) * scaleXY;
                    if (sy < lineTop - 1f || sy > lineBot + 1f) continue;
                    DrawDottedHLine(dl, lineLeft, lineRight, sy, ColGridMinor);
                }
            }
            {
                float first = (float)(Math.Ceiling((double)yMinV / major) * major);
                for (float y = first; y <= yMaxV + 1f; y += major)
                {
                    float sy = viewBot - (y - yLow) * scaleXY;
                    if (sy < lineTop - 1f || sy > lineBot + 1f) continue;
                    dl.AddLine(new Vector2(lineLeft, sy), new Vector2(lineRight, sy), ColGridMajor);
                }
            }

            // X (vertical) grid lines
            float xMinV = xLow + (lineLeft  - viewLeft) / scaleXY;
            float xMaxV = xLow + (lineRight - viewLeft) / scaleXY;

            if (minor > 0f)
            {
                float first = (float)(Math.Ceiling((double)xMinV / minor) * minor);
                for (float x = first; x <= xMaxV + 1f; x += minor)
                {
                    float sx = viewLeft + (x - xLow) * scaleXY;
                    if (sx < lineLeft - 1f || sx > lineRight + 1f) continue;
                    DrawDottedVLine(dl, sx, lineTop, lineBot, ColGridMinor);
                }
            }
            {
                float first = (float)(Math.Ceiling((double)xMinV / major) * major);
                for (float x = first; x <= xMaxV + 1f; x += major)
                {
                    float sx = viewLeft + (x - xLow) * scaleXY;
                    if (sx < lineLeft - 1f || sx > lineRight + 1f) continue;
                    dl.AddLine(new Vector2(sx, lineTop), new Vector2(sx, lineBot), ColGridMajor);
                }
            }
        }

        static void DrawGridLabels(ImDrawListPtr dl,
            float lblLeft, float lblRight, float lblTop, float lblBot,
            float lineLeft, float lineRight, float lineTop, float lineBot,
            float viewLeft, float viewBot, float xLow, float yLow, float scaleXY, string units,
            float major)
        {
            if (scaleXY <= 0) return;

            // Y (horizontal) labels
            float yMinV = yLow + (viewBot - lineBot) / scaleXY;
            float yMaxV = yLow + (viewBot - lineTop) / scaleXY;
            {
                float first = (float)(Math.Ceiling((double)yMinV / major) * major);
                for (float y = first; y <= yMaxV + 1f; y += major)
                {
                    float sy = viewBot - (y - yLow) * scaleXY;
                    if (sy < lineTop - 1f || sy > lineBot + 1f) continue;
                    string lbl = FormatInterval(y, major, units);
                    var tsz = ImGui.CalcTextSize(lbl);
                    DrawGridLabel(dl, lbl, tsz, lblLeft  + 8f,         sy - tsz.Y / 2f);
                    DrawGridLabel(dl, lbl, tsz, lblRight - tsz.X - 8f, sy - tsz.Y / 2f);
                }
            }

            // X (vertical) labels
            float xMinV = xLow + (lineLeft  - viewLeft) / scaleXY;
            float xMaxV = xLow + (lineRight - viewLeft) / scaleXY;
            {
                float first = (float)(Math.Ceiling((double)xMinV / major) * major);
                for (float x = first; x <= xMaxV + 1f; x += major)
                {
                    float sx = viewLeft + (x - xLow) * scaleXY;
                    if (sx < lineLeft - 1f || sx > lineRight + 1f) continue;
                    string lbl = FormatInterval(x, major, units);
                    var tsz = ImGui.CalcTextSize(lbl);
                    DrawGridLabel(dl, lbl, tsz, sx - tsz.X / 2f, lineTop - tsz.Y - 5f);
                    DrawGridLabel(dl, lbl, tsz, sx - tsz.X / 2f, lineBot + 5f);
                }
            }
        }

        static void DrawContourFill(ImDrawListPtr dl, Probe probe,
            float viewLeft, float viewBot, float xLow, float yLow, float scaleXY)
        {
            if (probe?.ProbePlanarContour == null || probe.ProbePlanarContour.Length < 3) return;
            var pts = new Vector2[probe.ProbePlanarContour.Length];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new Vector2(
                    viewLeft + ((float)probe.ProbePlanarContour[i][0] - xLow) * scaleXY,
                    viewBot  - ((float)probe.ProbePlanarContour[i][1] - yLow) * scaleXY);
            FillPolygon(dl, pts, ColContourFill);
        }

        static void FillPolygon(ImDrawListPtr dl, Vector2[] pts, uint col)
        {
            if (pts.Length < 3) return;
            float area = 0f;
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Length];
                area += a.X * b.Y - b.X * a.Y;
            }
            var idx = new List<int>(Enumerable.Range(0, pts.Length));
            while (idx.Count >= 3)
            {
                bool found = false;
                int n = idx.Count;
                for (int i = 0; i < n; i++)
                {
                    var a = pts[idx[(i - 1 + n) % n]];
                    var b = pts[idx[i]];
                    var c = pts[idx[(i + 1) % n]];
                    float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                    if (area * cross < 0f) continue;
                    bool ear = true;
                    for (int j = 0; j < n && ear; j++)
                    {
                        if (j == (i - 1 + n) % n || j == i || j == (i + 1) % n) continue;
                        var p = pts[idx[j]];
                        float d1 = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                        float d2 = (c.X - b.X) * (p.Y - b.Y) - (c.Y - b.Y) * (p.X - b.X);
                        float d3 = (a.X - c.X) * (p.Y - c.Y) - (a.Y - c.Y) * (p.X - c.X);
                        if (!((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0))) ear = false;
                    }
                    if (ear) { dl.AddTriangleFilled(a, b, c, col); idx.RemoveAt(i); found = true; break; }
                }
                if (!found) break;
            }
        }

        static void DrawGridLabel(ImDrawListPtr dl, string lbl, Vector2 tsz, float x, float y)
        {
            dl.AddText(new Vector2(x, y), ColRulerLabel, lbl);
        }

        static (float Major, float Minor) TickIntervals(float pxPerUnit, bool withMinor)
        {
            var major = TickCandidates[TickCandidates.Length - 1];
            foreach (var c in TickCandidates)
            {
                if (c * pxPerUnit >= MajorTickSpacingPx) { major = c; break; }
            }

            var minor = -1f;
            if (withMinor)
            {
                foreach (var c in TickCandidates)
                {
                    if (c >= major) break;
                    if (c * pxPerUnit >= MinorTickSpacingPx) { minor = c; break; }
                }
            }

            return (major, minor);
        }

        static string FormatInterval(float value, float interval, string units)
        {
            var decimals = interval >= 1f ? 0 : Math.Min(6, (int)Math.Ceiling(-Math.Log10(interval)));
            return value.ToString("F" + decimals) + units;
        }

        void DrawLegend(ImDrawListPtr dl, Vector2 cp, Vector2 avail)
        {
            if (Legend.Count == 0) return;

            const float pad = 5f;
            float lineH = ImGui.CalcTextSize("A").Y;
            float swatchSz = lineH;
            float rowH = lineH + pad;

            float maxEntryW = 0f;
            foreach (var e in Legend)
                maxEntryW = Math.Max(maxEntryW, swatchSz + pad + ImGui.CalcTextSize(e.Label).X);
            float boxW = maxEntryW + pad * 4f;
            float boxH = Legend.Count * rowH + pad * 3f;

            // Anchor inside the grid contact area (bottom-left), clear of axis-label margins
            float boxLeft = cp.X + GridMarginHPx + pad;
            float boxBottom = cp.Y + avail.Y - GridMarginVPx - pad;
            float boxTop = boxBottom - boxH;
            float boxRight  = boxLeft + boxW;

            dl.AddRectFilled(new Vector2(boxLeft, boxTop), new Vector2(boxRight, boxBottom), ColCoordOverlayBg, 3f);

            float x = boxLeft + pad * 2f;
            float y = boxTop + pad * 2f;

            foreach (var e in Legend)
            {
                var sMin = new Vector2(x, y);
                var sMax = new Vector2(x + swatchSz, y + swatchSz);
                if (e.OutlineOnly)
                {
                    dl.AddRect(sMin, sMax, e.Color, 0f, ContactSelBorderWidth);
                }
                else
                {
                    dl.AddRectFilled(sMin, sMax, e.Color);
                    if (e.DottedOutline)
                        DrawDottedRect(dl, sMin, sMax, ColBlockedOutline);
                }
                dl.AddText(new Vector2(x + swatchSz + pad, y), ColBottomInfoText, e.Label);
                y += rowH;
            }
        }

        static void DrawDottedHLine(ImDrawListPtr dl, float x0, float x1, float y, uint col)
        {
            const float dash = 3f, gap = 3f;
            for (float x = x0; x < x1; x += dash + gap)
                dl.AddLine(new Vector2(x, y), new Vector2(x + dash < x1 ? x + dash : x1, y), col, 1f);
        }

        static void DrawDottedVLine(ImDrawListPtr dl, float x, float y0, float y1, uint col)
        {
            const float dash = 3f, gap = 3f;
            for (float y = y0; y < y1; y += dash + gap)
                dl.AddLine(new Vector2(x, y), new Vector2(x, y + dash < y1 ? y + dash : y1), col, 1f);
        }

        static void DrawDottedRect(ImDrawListPtr dl, Vector2 min, Vector2 max, uint col)
        {
            DrawDottedHLine(dl, min.X, max.X, min.Y, col);
            DrawDottedHLine(dl, min.X, max.X, max.Y, col);
            DrawDottedVLine(dl, min.X, min.Y, max.Y, col);
            DrawDottedVLine(dl, max.X, min.Y, max.Y, col);
        }

        #endregion
    }
}
