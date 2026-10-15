using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Reactive.Linq;
using System.Windows.Forms;
using Bonsai.Design;
using Bonsai.Expressions;
using Hexa.NET.ImGui;
using OpenCV.Net;
using OpenEphys.EphysVisualization.Design.WinForms;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Provides a type visualizer that shows an <see cref="LfpViewer"/>'s matrices as a live multi-channel waveform
    /// viewer.
    /// </summary>
    public class LfpViewerVisualizer : DialogTypeVisualizer
    {
        const int DrainMilliseconds = 15;

        LfpViewer? node;
        readonly ConcurrentQueue<object> pending = new();
        Timer? drainTimer;
        ImGuiLfpViewerPanel? waveform;
        ControlStrip? strip;

        ImGuiHoverGLControl? canvas;
        DisplayPacer? pacer;

        // NB: shown in the plot's place rather than thrown. The viewer is a view on data it does not change, so a
        // mistake in it must not stop the workflow. Set on the thread the data arrives on, and read once a frame.
        volatile string? fault;
        float stripHeight;

        sealed record Sample(Mat Data, int SampleRate);

        sealed record Clipping(bool[] Channels);

        /// <inheritdoc/>
        public override void Load(IServiceProvider provider)
        {
            var context = (ITypeVisualizerContext)provider.GetService(typeof(ITypeVisualizerContext));
            node = (LfpViewer)ExpressionBuilder.GetWorkflowElement(context.Source);
            fault = null;

            // NB: no plot can be wider than the widest monitor unless its window spans two, so that is as much
            // horizontal detail as is ever worth holding.
            waveform = new(node, node.HistoryBytes, Screen.AllScreens.Max(screen => screen.Bounds.Width));
            var settings = node;
            waveform.Colors.GroupOf = channel =>
                settings.ColorGroupingEnabled ? channel / Math.Max(1, settings.ColorGrouping) : channel;

            strip = new ControlStrip(node);

            canvas = new ImGuiHoverGLControl(waveform.Hotkeys)
            {
                Size = new System.Drawing.Size(1100, 620),
                Dock = DockStyle.Fill
            };
            canvas.Render += RenderFrame;

            // NB: paced from the handle's lifetime rather than Load's, since a handle can be recreated.
            canvas.HandleCreated += (_, _) => pacer = new DisplayPacer(canvas.Handle);
            canvas.HandleDestroyed += (_, _) => StopPacer();
            canvas.Paint += (_, _) => pacer?.Painted();

            var visualizerService = (IDialogTypeVisualizerService)provider.GetService(typeof(IDialogTypeVisualizerService));
            visualizerService?.AddControl(canvas);

            // NB: each frame applies what has arrived, and so does a timer, which runs while the window is
            // minimized and paints nothing, so that what arrives meanwhile is applied rather than held.
            drainTimer = new Timer { Interval = DrainMilliseconds };
            drainTimer.Tick += (_, _) => Drain();
            drainTimer.Start();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Each matrix is converted where it arrives and queued, and the queue is applied at the start of each
        /// frame, rather than handed to <see cref="Show"/> one value at a time as the base class does, which at a
        /// high rate would keep the window from painting.
        /// </remarks>
        public override IObservable<object> Visualize(IObservable<IObservable<object>> source, IServiceProvider provider) =>
            Observable.Create<object>(_ => source.Merge().Cast<Mat>().Subscribe(Receive, ex => fault = ex.Message));

        void Receive(Mat raw)
        {
            var settings = node;
            if (settings is null || fault is not null)
                return;

            if (raw.Channels != 1)
            {
                fault = $"LfpViewer: each element of the matrix must be a single value, not {raw.Channels}.";
                return;
            }

            if (settings.SampleRate < 1)
            {
                fault = "LfpViewer: SampleRate must be at least 1 Hz.";
                return;
            }

            if (settings.ShowClipping)
            {
                var (depthLow, depthHigh) = DepthLimits(raw.Depth);
                var clipped = ClippedRows(raw, settings.SaturationLow ?? depthLow, settings.SaturationHigh ?? depthHigh);
                if (clipped is not null)
                    pending.Enqueue(new Clipping(clipped));
            }

            var data = new Mat(raw.Rows, raw.Cols, Depth.F32, 1);
            CV.ConvertScale(raw, data, settings.Scale, -settings.Offset * settings.Scale);
            pending.Enqueue(new Sample(data, settings.SampleRate));
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

        void Drain()
        {
            while (pending.TryDequeue(out var value))
                Apply(value);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Nothing is shown through here, since <see cref="Visualize"/> applies what the viewer receives itself.
        /// </remarks>
        public override void Show(object value)
        {
        }

        void Apply(object value)
        {
            if (waveform is null)
                return;

            if (value is Clipping clipping)
            {
                waveform.MarkClipping(clipping.Channels);
                return;
            }

            using var data = ((Sample)value).Data;
            waveform.Update(data, ((Sample)value).SampleRate);
        }

        void RenderFrame(object? sender, EventArgs e)
        {
            if (canvas is null || waveform is null || strip is null)
                return;

            Drain();
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(canvas.Width, canvas.Height);

            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(io.DisplaySize);
            ImGui.Begin("LFP Viewer##lfpviewer",
                ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

            // NB: measured from the last frame rather than derived from the widgets, which are the
            // strip's business and change as columns are added.
            if (stripHeight <= 0)
                stripHeight = ImGui.GetTextLineHeight() + ImGui.GetFrameHeight() * 2;

            var maximized = waveform.Maximized;
            var availY = ImGui.GetContentRegionAvail().Y - (maximized ? 0 : stripHeight);

            ImGui.BeginChild("##wavePane", new Vector2(0, availY));
            var message = fault;
            if (message is null)
            {
                waveform.RefreshRate = pacer?.RefreshRate ?? waveform.RefreshRate;
                waveform.Draw();
            }
            else
            {
                // NB: TextUnformatted rather than TextWrapped, which reads its string as a printf format,
                // and the message is exception text that may contain '%'.
                ImGui.PushTextWrapPos();
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiPalette.VibrantCoral);
                ImGui.TextUnformatted(message);
                ImGui.PopStyleColor();
                ImGui.TextUnformatted("Close this window, correct the input or the LfpViewer's settings, and open it again.");
                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();

            if (!maximized)
            {
                var stripTop = ImGui.GetCursorScreenPos().Y;
                strip.Draw(waveform);
                stripHeight = ImGui.GetCursorScreenPos().Y - stripTop;
            }

            ImGui.End();
        }

        void StopPacer()
        {
            pacer?.Dispose();
            pacer = null;
        }

        /// <inheritdoc/>
        public override void Unload()
        {
            drainTimer?.Dispose();
            while (pending.TryDequeue(out var value))
            {
                if (value is Sample sample)
                    sample.Data.Dispose();
            }
            StopPacer();
            waveform?.Dispose();
            canvas?.Dispose();

            strip = null;
            waveform = null;
            canvas = null;
            drainTimer = null;
            node = null;
        }

        /// <summary>
        /// The standard strip, with the grouping of channel colors the viewer's node saves.
        /// </summary>
        sealed class ControlStrip : ImGuiLfpViewerControlStrip
        {
            readonly LfpViewer node;

            public ControlStrip(LfpViewer node) => this.node = node;

            private protected override int ParameterColumns => base.ParameterColumns + 1;

            private protected override void TrailingColumns(ImGuiLfpViewerPanel panel)
            {
                var width = MenuColumn("Color Groups");
                var enabled = node.ColorGroupingEnabled;
                if (ImGui.Checkbox("##colorGroupingEnabled", ref enabled))
                    node.ColorGroupingEnabled = enabled;
                ImGuiWidgets.Tooltip("Color neighboring channels alike, in groups of the size beside it, to tell " +
                    "groups of channels apart.");

                ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
                ImGui.BeginDisabled(!enabled);
                var grouping = node.ColorGrouping;
                ImGui.SetNextItemWidth(width - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemInnerSpacing.X);
                if (ImGui.InputInt("##colorGrouping", ref grouping, 1))
                    node.ColorGrouping = Math.Max(1, grouping);
                ImGuiWidgets.Tooltip("How many neighboring channels share a color.",
                    "Check the box beside it to color channels in groups.");
                ImGui.EndDisabled();
            }
        }
    }
}
