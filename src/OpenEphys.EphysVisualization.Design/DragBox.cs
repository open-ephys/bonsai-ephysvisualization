using System.Numerics;
using Hexa.NET.ImGui;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// The rectangle a <see cref="DragBox"/> spans, in screen coordinates.
    /// </summary>
    /// <param name="Min">The top left corner.</param>
    /// <param name="Max">The bottom right corner.</param>
    /// <param name="Wide">Whether it reaches past the drag threshold across, so that it means something along x.</param>
    /// <param name="Tall">Whether it reaches past the drag threshold down, so that it means something along y.</param>
    internal readonly record struct DragRect(Vector2 Min, Vector2 Max, bool Wide, bool Tall);

    /// <summary>
    /// A box drawn out by dragging with one mouse button, from where the drag began to the pointer.
    /// </summary>
    /// <remarks>
    /// Only the box: which press begins one, and what it does while held and when let go, are the caller's.
    /// </remarks>
    internal sealed class DragBox
    {
        readonly ImGuiMouseButton button;
        Vector2? start;

        public DragBox(ImGuiMouseButton button)
        {
            this.button = button;
        }

        public bool Dragging => start is not null;

        /// <summary>
        /// Begins a box at the pointer.
        /// </summary>
        public void Begin() => start = ImGui.GetMousePos();

        public void Cancel() => start = null;

        /// <summary>
        /// The box so far, within <paramref name="min"/> and <paramref name="max"/>, or null when none is being
        /// dragged.
        /// </summary>
        public DragRect? Current(Vector2 min, Vector2 max) =>
            start is { } from ? Span(from, ImGui.GetMousePos(), min, max) : null;

        /// <summary>
        /// Ends the box on the frame its button is let go, giving what it spanned within <paramref name="min"/> and
        /// <paramref name="max"/>.
        /// </summary>
        /// <returns>False while the button is held, or when no box is being dragged.</returns>
        public bool Released(Vector2 min, Vector2 max, out DragRect box)
        {
            box = default;
            if (start is not { } from || ImGui.IsMouseDown(button))
                return false;

            box = Span(from, ImGui.GetMousePos(), min, max);
            start = null;
            return true;
        }

        static DragRect Span(Vector2 from, Vector2 to, Vector2 min, Vector2 max)
        {
            var low = Vector2.Clamp(Vector2.Min(from, to), min, max);
            var high = Vector2.Clamp(Vector2.Max(from, to), min, max);
            var threshold = ImGui.GetIO().MouseDragThreshold;
            return new DragRect(low, high, high.X - low.X > threshold, high.Y - low.Y > threshold);
        }
    }
}
