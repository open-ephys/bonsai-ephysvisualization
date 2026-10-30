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
    /// Provides an abstract base class for type visualizers that show a <see cref="WaveformScopeBuilder"/>'s data as a
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
    public abstract class WaveformScopeVisualizerBase : DialogTypeVisualizer
    {
        const int DrainMilliseconds = 15;

        readonly ConcurrentQueue<Pending> pending = new();
        WaveformScopeBuilder? scope;
        ImGuiWaveformPanel? panel;
        ImGuiWaveformControlStrip? strip;
        StandardColumns? standardColumns;
        ImGuiHoverGLControl? canvas;
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
        protected WaveformScopeBuilder Scope => scope ?? throw NotLoaded();

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
        /// Gets or sets the function giving the group a channel takes its color from, or null, the default, for
        /// each row to take the next color of the palette. Channels sharing a group share a color.
        /// </summary>
        /// <remarks>
        /// Without one, the colors follow the rows, so that neighboring rows differ whichever channels <see
        /// cref="Order"/> puts side by side. Called for every channel every frame, so a function that reads
        /// settings as it goes never has to be told they changed.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The visualizer is not loaded.</exception>
        protected Func<int, int>? ChannelGroup
        {
            get => Panel.Colors.GroupOf;
            set => Panel.Colors.GroupOf = value;
        }

        /// <summary>
        /// Gets or sets the channel each row of the posted data shows, from the top, and where each row lies, or null
        /// for each row showing the channel of its own index, the rows evenly spaced.
        /// </summary>
        /// <remarks>
        /// For an owner that reorders the rows it posts, as by where their electrodes sit, with the clipping flags in
        /// the same order. Labels, hidden channels, colors and readouts then follow the channel rather than the row.
        /// A change of channels discards what the display holds, so set it through <see cref="Post"/> ahead of the
        /// first block in the new order, and only while not paused. A change of positions alone keeps it.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The visualizer is not loaded.</exception>
        /// <exception cref="ArgumentException">
        /// The positions are not one per channel, or one is less than the one before it.
        /// </exception>
        protected RowOrder? Order
        {
            get => Panel.Order;
            set => Panel.Order = value;
        }

        /// <summary>
        /// Gets or sets the channel selected in the plot, which the cursors read, or -1 for none. Setting it to a
        /// hidden channel, or one the data does not show, selects none.
        /// </summary>
        /// <remarks>
        /// Read and set it while drawing, as from <see cref="DrawFrame"/>, to follow or make a selection elsewhere,
        /// such as on a probe.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The visualizer is not loaded.</exception>
        protected int SelectedChannel
        {
            get => Panel.SelectedChannel;
            set => Panel.SelectedChannel = value;
        }

        /// <summary>
        /// Gets or sets the channel hovered in the plot as of the last frame drawn, or -1 for none: the one under the
        /// pointer, or else the one set. A channel set is drawn as one under the pointer is, and not scrolled to.
        /// </summary>
        /// <remarks>
        /// Set it each frame, as from <see cref="DrawFrame"/>, to the channel hovered elsewhere, such as on a probe,
        /// or to -1 when there is none.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The visualizer is not loaded.</exception>
        protected int HoveredChannel
        {
            get => Panel.HoveredChannel;
            set => Panel.HoveredChannel = value;
        }

        /// <summary>
        /// Brings a channel into view in the plot on the next frame drawn: centered, in its lane widened if that is
        /// shrunk to a strip, or expanded in place of the channel that is. Does nothing for a channel the data does
        /// not show.
        /// </summary>
        /// <param name="channel">The channel to show.</param>
        /// <exception cref="InvalidOperationException">The visualizer is not loaded.</exception>
        protected void Reveal(int channel) => Panel.Reveal(channel);

        /// <summary>
        /// Fits channels to the plot's height, as a zoom box over them does: the rows from the highest of them to the
        /// lowest fill the plot, with no channel expanded and, where they span lanes, every lane widened. Hidden
        /// channels, and ones the data does not show, are left out.
        /// </summary>
        /// <remarks>
        /// The rows between them are shown too, so channels close together on a probe fit tightly only in an order
        /// that keeps them together, such as by depth.
        /// </remarks>
        /// <param name="channels">The channels to fit.</param>
        /// <exception cref="InvalidOperationException">The visualizer is not loaded.</exception>
        protected void ZoomTo(IEnumerable<int> channels) => Panel.ZoomTo(channels);

        /// <summary>
        /// Gets or sets whether the pointer is over a pane beside the plot that answers keys of its own, such as a
        /// probe, so the plot leaves every key to it. Elsewhere in the window, the control strip included, the plot
        /// answers its keys.
        /// </summary>
        /// <remarks>
        /// Set it each frame, as from <see cref="DrawFrame"/>, before <see cref="DrawPlot"/>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The visualizer is not loaded.</exception>
        protected bool KeysElsewhere
        {
            get => Panel.KeysElsewhere;
            set => Panel.KeysElsewhere = value;
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
        protected virtual string Unit => string.Empty;

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
            scope = (WaveformScopeBuilder)ExpressionBuilder.GetWorkflowElement(context.Source);
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
        /// Marks the rows set in <paramref name="rows"/> as clipping now, from any thread, in order with the
        /// data.
        /// </summary>
        /// <param name="rows">One flag per row of the posted data, set where a sample reached either end of its range.</param>
        protected void PostClipping(bool[] rows) => pending.Enqueue(new(null, 0, rows, null));

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
                else if (item.Clipping is { } rows)
                    panel?.MarkClipping(rows);
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
            // NB: no keyboard navigation, which would take the arrows that step the cursors.
            ImGui.Begin("##waveformScope",
                ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoNavInputs);
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
                waveform.RefreshRate = canvas?.RefreshRate ?? waveform.RefreshRate;
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

        /// <inheritdoc/>
        /// <remarks>
        /// A derived class that overrides this calls it last.
        /// </remarks>
        public override void Unload()
        {
            drainTimer?.Dispose();
            while (pending.TryDequeue(out var item))
                item.Data?.Dispose();
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
