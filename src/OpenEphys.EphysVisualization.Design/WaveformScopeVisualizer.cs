using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using Bonsai.Design;
using Bonsai.Expressions;
using Hexa.NET.ImGui;
using OpenCV.Net;
using OpenEphys.EphysVisualization.Design.WinForms;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Provides an abstract base class for type visualizers that show a <see cref="WaveformScope"/>'s data as a
    /// live multi-channel waveform viewer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base class owns the window, its pacing to the display, the waveform plot and the control strip. A
    /// derived class turns what the node receives into blocks of samples, in <see cref="Visualize"/>, and hands them
    /// over with <see cref="PostData"/>; anything else it needs done on the window's thread it hands over with
    /// <see cref="Post"/>, in order with the data.
    /// </para>
    /// <para>
    /// Each frame calls <see cref="DrawFrame"/>, which by default draws the plot above a control strip of the
    /// <see cref="StandardColumns"/>. A derived class that shows more overrides it and arranges
    /// <see cref="DrawPlot"/> and <see cref="DrawControlStrip"/>, with whichever columns it chooses, around its own
    /// content.
    /// </para>
    /// </remarks>
    public abstract class WaveformScopeVisualizer : DialogTypeVisualizer
    {
        const int DrainMilliseconds = 15;

        readonly ConcurrentQueue<Pending> pending = new();
        WaveformScope? scope;
        ImGuiWaveformPanel? panel;
        ImGuiWaveformControlStrip? strip;
        StandardColumns? standardColumns;
        ImGuiHoverGLControl? canvas;
        DisplayPacer? pacer;
        Timer? drainTimer;

        // NB: shown in the plot's place rather than thrown. The viewer is a view on data it does not change, so a
        // mistake in it must not stop the workflow. Set on whichever thread finds it, and read once a frame.
        volatile string? fault;
        float stripHeight;

        readonly record struct Pending(Mat? Data, int SampleRate, bool[]? Clipping, Action? Work);

        /// <summary>
        /// Gets the node whose data is shown and whose settings the viewer reads and writes.
        /// </summary>
        /// <exception cref="InvalidOperationException">The visualizer is not loaded.</exception>
        protected WaveformScope Scope => scope ?? throw NotLoaded();

        /// <summary>
        /// Gets the control strip columns for the settings every waveform viewer has, for
        /// <see cref="DrawControlStrip"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">The visualizer is not loaded.</exception>
        protected StandardColumns StandardColumns => standardColumns ?? throw NotLoaded();

        ImGuiWaveformPanel Panel => panel ?? throw NotLoaded();

        static InvalidOperationException NotLoaded() => new("The visualizer is not loaded.");

        /// <summary>
        /// Gets a value indicating whether the display is paused.
        /// </summary>
        protected bool Paused => panel?.Paused ?? false;

        /// <summary>
        /// Gets a value indicating whether the plot is given the whole window, so that whatever else a frame draws
        /// is left out.
        /// </summary>
        protected bool Maximized => panel?.Maximized ?? false;

        /// <summary>
        /// Gets or sets the function giving the group a channel takes its color from. Channels sharing a group share
        /// a color.
        /// </summary>
        /// <remarks>
        /// Called for every channel every frame, so a function that reads settings as it goes never has to be told
        /// they changed. By default each channel is its own group.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The visualizer is not loaded.</exception>
        protected Func<int, int> ChannelGroup
        {
            get => Panel.Colors.GroupOf;
            set => Panel.Colors.GroupOf = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets a value indicating whether a fault has been reported, after which the viewer shows it in place of
        /// the plot until it is closed.
        /// </summary>
        protected bool Faulted => fault is not null;

        /// <summary>
        /// Gets the memory, in bytes, given to the data retained behind the display. Read once, when the visualizer
        /// loads.
        /// </summary>
        protected abstract long HistoryBytes { get; }

        /// <summary>
        /// Gets the unit of the samples shown, or an empty string where they have none. Read once, when the
        /// visualizer loads.
        /// </summary>
        /// <remarks>
        /// By default <c>a.u.</c>, arbitrary units, so that a viewer which cannot know what its samples measure says
        /// so rather than showing bare numbers.
        /// </remarks>
        protected virtual string Unit => "a.u.";

        /// <summary>
        /// Gets the keys, besides the plot's own, that whatever a derived class draws answers while the pointer is
        /// over the window. Read once, when the visualizer loads.
        /// </summary>
        protected virtual IEnumerable<ImGuiKey> Hotkeys => Enumerable.Empty<ImGuiKey>();

        /// <summary>
        /// Gets the line shown under a fault, saying how to put it right.
        /// </summary>
        protected virtual string FaultHint => "Close this window, correct the cause, and open it again.";

        /// <inheritdoc/>
        /// <remarks>
        /// A derived class that overrides this calls it first, after which <see cref="Scope"/> and
        /// <see cref="StandardColumns"/> are available.
        /// </remarks>
        public override void Load(IServiceProvider provider)
        {
            var context = (ITypeVisualizerContext)provider.GetService(typeof(ITypeVisualizerContext));
            scope = (WaveformScope)ExpressionBuilder.GetWorkflowElement(context.Source);
            fault = null;

            // NB: no plot can be wider than the widest monitor unless its window spans two, so that is as much
            // horizontal detail as is ever worth holding.
            panel = new(scope, HistoryBytes, Screen.AllScreens.Max(screen => screen.Bounds.Width)) { Unit = Unit };
            strip = new();
            standardColumns = new(panel);

            var host = new ImGuiHoverGLControl(panel.Hotkeys.Concat(Hotkeys))
            {
                Size = new System.Drawing.Size(1100, 620),
                Dock = DockStyle.Fill
            };
            host.Render += RenderFrame;

            // NB: paced from the handle's lifetime rather than Load's, since a handle can be recreated.
            host.HandleCreated += (_, _) => pacer = new DisplayPacer(host.Handle);
            host.HandleDestroyed += (_, _) => StopPacer();
            host.Paint += (_, _) => pacer?.Painted();
            canvas = host;

            var visualizerService = (IDialogTypeVisualizerService)provider.GetService(typeof(IDialogTypeVisualizerService));
            visualizerService?.AddControl(host);

            // NB: each frame applies what has arrived, and so does a timer, which runs while the window is
            // minimized and paints nothing, so that what arrives meanwhile is applied rather than held.
            drainTimer = new Timer { Interval = DrainMilliseconds };
            drainTimer.Tick += (_, _) => Drain();
            drainTimer.Start();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A derived class turns what the node receives into blocks for <see cref="PostData"/>, rather than having it
        /// handed to <see cref="Show"/> one value at a time as the base class does, which at a high rate would keep
        /// the window from painting.
        /// </remarks>
        public abstract override IObservable<object> Visualize(IObservable<IObservable<object>> source, IServiceProvider provider);

        /// <inheritdoc/>
        /// <remarks>
        /// Nothing is shown through here, since <see cref="Visualize"/> hands the data over itself.
        /// </remarks>
        public sealed override void Show(object value)
        {
        }

        /// <summary>
        /// Hands over a block of samples to be shown, from any thread. Blocks are shown in the order they are posted.
        /// </summary>
        /// <param name="data">
        /// A channel-by-sample matrix of 32-bit floats, one row per channel. The viewer takes ownership of it and
        /// disposes it once shown, so it must not be used again.
        /// </param>
        /// <param name="sampleRate">The rate, in Hz, at which each channel is sampled.</param>
        protected void PostData(Mat data, int sampleRate) => pending.Enqueue(new(data, sampleRate, null, null));

        /// <summary>
        /// Marks the channels set in <paramref name="channels"/> as clipping now, from any thread, in order with the
        /// data.
        /// </summary>
        /// <param name="channels">One flag per channel, set where a sample reached either end of its range.</param>
        protected void PostClipping(bool[] channels) => pending.Enqueue(new(null, 0, channels, null));

        /// <summary>
        /// Runs <paramref name="action"/> on the window's thread, in order with the data, from any thread.
        /// </summary>
        /// <param name="action">The work to run.</param>
        protected void Post(Action action) => pending.Enqueue(new(null, 0, null, action));

        /// <summary>
        /// Reports a fault, from any thread, which the viewer shows in place of the plot with
        /// <see cref="FaultHint"/> under it.
        /// </summary>
        /// <param name="message">What went wrong.</param>
        protected void Fail(string message) => fault = message;

        void Drain()
        {
            while (pending.TryDequeue(out var item))
            {
                if (item.Data is { } data)
                {
                    using (data)
                        panel?.Update(data, item.SampleRate);
                }
                else if (item.Clipping is { } channels)
                    panel?.MarkClipping(channels);
                else
                    item.Work?.Invoke();
            }
        }

        void RenderFrame(object? sender, EventArgs e)
        {
            if (canvas is null || panel is null)
                return;

            Drain();
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(canvas.Width, canvas.Height);

            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(io.DisplaySize);
            ImGui.Begin("##waveformScope",
                ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            DrawFrame();
            ImGui.End();
        }

        /// <summary>
        /// Draws the window's contents, once per frame, into the window the base class has opened. By default the
        /// plot fills the window above a control strip of the <see cref="StandardColumns"/>, which is left out while
        /// <see cref="Maximized"/>.
        /// </summary>
        protected virtual void DrawFrame()
        {
            var plotHeight = ImGui.GetContentRegionAvail().Y - (Maximized ? 0 : ControlStripHeight);
            DrawPlot(new Vector2(0, plotHeight));
            if (!Maximized)
                DrawControlStrip(StandardColumns.All);
        }

        /// <summary>
        /// Draws the waveform plot at the cursor, or the fault in its place.
        /// </summary>
        /// <param name="size">
        /// The size of the plot in pixels. A component of zero or less fills the space left in that direction, as
        /// ImGui sizes a child window.
        /// </param>
        protected void DrawPlot(Vector2 size)
        {
            var waveform = Panel;
            ImGui.BeginChild("##plot", size);
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
                ImGui.TextUnformatted(FaultHint);
                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();
        }

        /// <summary>
        /// Draws a control strip of <paramref name="columns"/> at the cursor, across the width left, with pause and
        /// the record length beside them.
        /// </summary>
        /// <remarks>
        /// The columns are laid out in order on a grid that wraps to the width. A standard column left out leaves
        /// the plot's gestures acting on its setting with nothing on screen showing it; see
        /// <see cref="OpenEphys.EphysVisualization.Design.StandardColumns"/>.
        /// </remarks>
        /// <param name="columns">The columns to draw, in order.</param>
        /// <exception cref="ArgumentException">A column has no draw action.</exception>
        protected void DrawControlStrip(IEnumerable<ControlStripColumn> columns)
        {
            var list = columns as IReadOnlyList<ControlStripColumn> ?? columns.ToList();
            foreach (var column in list)
            {
                if (column.Draw is null)
                    throw new ArgumentException("A column needs a draw action.", nameof(columns));
            }

            var top = ImGui.GetCursorScreenPos().Y;
            (strip ?? throw NotLoaded()).Draw(Panel, list);
            stripHeight = ImGui.GetCursorScreenPos().Y - top;
        }

        /// <summary>
        /// Gets the height in pixels of the control strip as last drawn, for leaving room for it.
        /// </summary>
        /// <remarks>
        /// Measured from the last frame rather than derived from the widgets, which change as columns wrap and the
        /// strip is folded away. Before the strip has been drawn, an estimate.
        /// </remarks>
        protected float ControlStripHeight =>
            stripHeight > 0 ? stripHeight : ImGui.GetTextLineHeight() + ImGui.GetFrameHeight() * 2;

        void StopPacer()
        {
            pacer?.Dispose();
            pacer = null;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A derived class that overrides this calls it last.
        /// </remarks>
        public override void Unload()
        {
            drainTimer?.Dispose();
            while (pending.TryDequeue(out var item))
                item.Data?.Dispose();
            StopPacer();
            panel?.Dispose();
            canvas?.Dispose();

            strip = null;
            standardColumns = null;
            panel = null;
            canvas = null;
            drainTimer = null;
            scope = null;
        }
    }
}
