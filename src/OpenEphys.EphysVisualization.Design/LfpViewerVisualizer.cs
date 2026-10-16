using System;
using System.Reactive.Linq;
using Hexa.NET.ImGui;
using OpenCV.Net;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Provides a type visualizer that shows an <see cref="LfpViewer"/>'s matrices as a live multi-channel waveform
    /// viewer.
    /// </summary>
    public class LfpViewerVisualizer : WaveformScopeVisualizer
    {
        LfpViewer Viewer => (LfpViewer)Scope;

        /// <inheritdoc/>
        protected override long HistoryBytes => Viewer.HistoryBytes;

        /// <inheritdoc/>
        protected override string FaultHint =>
            "Close this window, correct the input or the LfpViewer's settings, and open it again.";

        /// <inheritdoc/>
        public override void Load(IServiceProvider provider)
        {
            base.Load(provider);
            var viewer = Viewer;
            ChannelGroup = channel =>
                viewer.ColorGroupingEnabled ? channel / Math.Max(1, viewer.ColorGrouping) : channel;

            var standard = StandardColumns;
            columns = new[]
            {
                standard.Timebase, standard.ChannelHeight, standard.Range, standard.Palette,
                new ControlStripColumn("Color Groups", ColorGroupsColumn), standard.Display,
            };
        }

        ControlStripColumn[] columns = Array.Empty<ControlStripColumn>();

        /// <inheritdoc/>
        /// <remarks>
        /// The standard columns, with the grouping of channel colors after the palette.
        /// </remarks>
        protected override void DrawFrame()
        {
            var plotHeight = ImGui.GetContentRegionAvail().Y - (Maximized ? 0 : ControlStripHeight);
            DrawPlot(new System.Numerics.Vector2(0, plotHeight));
            if (!Maximized)
                DrawControlStrip(columns);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Each matrix is checked for clipping and converted to the samples shown on the thread it arrives on.
        /// </remarks>
        public override IObservable<object> Visualize(IObservable<IObservable<object>> source, IServiceProvider provider)
        {
            // NB: the node is taken once here rather than on each block, which arrives on the workflow's thread and
            // may do so after the window has unloaded.
            var viewer = Viewer;
            return Observable.Create<object>(_ => source.Merge().Cast<Mat>()
                .Subscribe(raw => Receive(viewer, raw), ex => Fail(ex.Message)));
        }

        void Receive(LfpViewer viewer, Mat raw)
        {
            if (Faulted)
                return;

            if (raw.Channels != 1)
            {
                Fail($"LfpViewer: each element of the matrix must be a single value, not {raw.Channels}.");
                return;
            }

            if (viewer.SampleRate < 1)
            {
                Fail("LfpViewer: SampleRate must be at least 1 Hz.");
                return;
            }

            if (viewer.ShowClipping)
            {
                var (depthLow, depthHigh) = DepthLimits(raw.Depth);
                var clipped = ClippedRows(raw, viewer.SaturationLow ?? depthLow, viewer.SaturationHigh ?? depthHigh);
                if (clipped is not null)
                    PostClipping(clipped);
            }

            var data = new Mat(raw.Rows, raw.Cols, Depth.F32, 1);
            CV.ConvertScale(raw, data, viewer.Scale, -viewer.Offset * viewer.Scale);
            PostData(data, viewer.SampleRate);
        }

        static (double? Low, double? High) DepthLimits(Depth depth) => depth switch
        {
            Depth.U8 => (byte.MinValue, byte.MaxValue),
            Depth.S8 => (sbyte.MinValue, sbyte.MaxValue),
            Depth.U16 => (ushort.MinValue, ushort.MaxValue),
            Depth.S16 => (short.MinValue, short.MaxValue),
            Depth.S32 => (int.MinValue, int.MaxValue),
            _ => (null, null),
        };

        /// <summary>
        /// The rows of <paramref name="raw"/> holding a sample at or beyond either limit, or null where none does.
        /// </summary>
        /// <remarks>
        /// Null when nothing clipped, so a clean recording sends the display nothing.
        /// </remarks>
        static bool[]? ClippedRows(Mat raw, double? low, double? high)
        {
            if (low is null && high is null)
                return null;

            bool[]? clipped = null;
            for (int row = 0; row < raw.Rows; row++)
            {
                using var samples = raw.GetRow(row);
                CV.MinMaxLoc(samples, out var min, out var max, out _, out _);
                if (min <= low || max >= high)
                {
                    clipped ??= new bool[raw.Rows];
                    clipped[row] = true;
                }
            }

            return clipped;
        }

        void ColorGroupsColumn(float width)
        {
            var viewer = Viewer;
            var enabled = viewer.ColorGroupingEnabled;
            if (ImGui.Checkbox("##colorGroupingEnabled", ref enabled))
                viewer.ColorGroupingEnabled = enabled;
            ImGuiWidgets.Tooltip("Color neighboring channels alike, in groups of the size beside it, to tell " +
                "groups of channels apart.");

            ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.BeginDisabled(!enabled);
            var grouping = viewer.ColorGrouping;
            ImGui.SetNextItemWidth(width - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemInnerSpacing.X);
            if (ImGui.InputInt("##colorGrouping", ref grouping, 1))
                viewer.ColorGrouping = Math.Max(1, grouping);
            ImGuiWidgets.Tooltip("How many neighboring channels share a color.",
                "Check the box beside it to color channels in groups.");
            ImGui.EndDisabled();
        }
    }
}
