using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using Hexa.NET.ImGui;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Shows the spike waveforms detected on each channel as persistence: every waveform added is drawn once into a
    /// density that is kept until cleared, so separate units on a channel show as separate bands, a rare one beside a
    /// busy one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The densities are kept on the GPU, one tile per channel, in the signal's own coordinates: time across, and
    /// amplitude in bins spaced evenly in its inverse hyperbolic sine, fine near zero and coarser at large amplitudes.
    /// So the amplitudes drawn can change at any time without losing what has accumulated, and no waveform is kept
    /// once it has been drawn.
    /// </para>
    /// <para>
    /// The view detects nothing itself. Its owner adds waveforms with <see cref="Add"/>, from any thread, and draws a
    /// channel's density wherever it chooses with <see cref="Draw"/>. Every channel accumulates whether or not it is
    /// drawn, as long as <see cref="Flush"/> or <see cref="Draw"/> is called each frame.
    /// </para>
    /// </remarks>
    public sealed class ImGuiSpikeView
    {
        // NB: columns per sample, so the line between two samples has room to be drawn as a line.
        const int Oversample = 4;
        const int Bins = 1024;

        // NB: how far from the threshold's line, in pixels, the pointer still takes hold of it.
        const float ThresholdGrab = 4;

        readonly int channels;
        readonly float span;
        readonly float knee;
        readonly ConcurrentQueue<(int Channel, float[] Waveform, Shape Shape)> pending = new();
        readonly long[] spikeCounts;
        int clearRequested;

        // NB: set from any thread, and taken up by the atlas on the next frame, after which a waveform added in an
        // older shape is dropped.
        volatile Shape shape;
        Shape current;

        sealed record Shape(int Length, int Delay, double SampleRate)
        {
            public int TileWidth { get; } = ImGuiSpikeView.TileWidth(Length);
        }

        readonly Gpu gpu;
        readonly List<float> vertices = new();
        bool draggingThreshold;
        bool thresholdMoved;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImGuiSpikeView"/> class.
        /// </summary>
        /// <param name="channels">The number of channels.</param>
        /// <param name="length">The number of samples in each waveform.</param>
        /// <param name="delay">The number of samples in each waveform before its threshold crossing.</param>
        /// <param name="sampleRate">The rate the waveforms are sampled at, in Hz.</param>
        /// <param name="span">
        /// The largest amplitude kept, either side of zero, in the waveforms' units. A sample beyond it is drawn at
        /// its edge.
        /// </param>
        public ImGuiSpikeView(int channels, int length, int delay, double sampleRate, double span)
        {
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            if (span <= 0) throw new ArgumentOutOfRangeException(nameof(span));

            this.channels = channels;
            shape = current = Validated(length, delay, sampleRate);
            this.span = (float)span;
            knee = Knee(this.span);
            spikeCounts = new long[channels];
            gpu = new Gpu(this);
        }

        static Shape Validated(int length, int delay, double sampleRate)
        {
            if (length < 2) throw new ArgumentOutOfRangeException(nameof(length));
            if (delay < 0 || delay >= length) throw new ArgumentOutOfRangeException(nameof(delay));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            return new Shape(length, delay, sampleRate);
        }

        /// <summary>
        /// Changes the waveforms' length, delay and rate, from any thread, which empties every channel's density,
        /// since what has accumulated was drawn in the old shape.
        /// </summary>
        /// <param name="length">The number of samples in each waveform.</param>
        /// <param name="delay">The number of samples in each waveform before its threshold crossing.</param>
        /// <param name="sampleRate">The rate the waveforms are sampled at, in Hz.</param>
        public void Reshape(int length, int delay, double sampleRate)
        {
            shape = Validated(length, delay, sampleRate);
            Clear();
        }

        /// <summary>
        /// Gets or sets the unit of the waveforms' amplitudes, shown in the readings.
        /// </summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>
        /// Adds a detected waveform to a channel's density, from any thread. It is drawn in on the next frame.
        /// </summary>
        /// <param name="channel">The channel it was detected on. One out of range is ignored.</param>
        /// <param name="waveform">
        /// Its samples, at least the view's length of them. A copy is kept until it is drawn, so the array can be
        /// reused at once.
        /// </param>
        public void Add(int channel, float[] waveform)
        {
            var s = shape;
            if ((uint)channel < (uint)channels && waveform.Length >= s.Length)
            {
                var copy = new float[s.Length];
                Array.Copy(waveform, copy, s.Length);
                pending.Enqueue((channel, copy, s));
            }
        }

        /// <summary>
        /// Empties every channel's density, from any thread, including waveforms added but not yet drawn.
        /// </summary>
        public void Clear()
        {
            Interlocked.Exchange(ref clearRequested, 1);
            while (pending.TryDequeue(out _)) { }
        }

        /// <summary>
        /// Gets the number of waveforms drawn into a channel's density since it was last cleared.
        /// </summary>
        /// <param name="channel">The channel.</param>
        /// <returns>The number of waveforms.</returns>
        public long SpikeCount(int channel) => (uint)channel < (uint)channels ? spikeCounts[channel] : 0;

        /// <summary>
        /// Draws the waveforms added since the last frame into their channels' densities. Call it once a frame while
        /// no channel is drawn, with the window's GL context current; <see cref="Draw"/> calls it too.
        /// </summary>
        public void Flush()
        {
            if (!gpu.Ready())
                return;

            var s = shape;
            if (s != current)
            {
                if (s.TileWidth != current.TileWidth)
                    gpu.Resize(s.TileWidth);
                current = s;
                if (!gpu.Ready())
                    return;
            }

            if (Interlocked.Exchange(ref clearRequested, 0) == 1)
            {
                gpu.ClearAtlas();
                Array.Clear(spikeCounts, 0, spikeCounts.Length);
            }

            vertices.Clear();
            while (pending.TryDequeue(out var spike))
            {
                if (spike.Shape != current)
                    continue;
                var (x, y) = TileOrigin(spike.Channel, gpu.TilesPerRow, current.TileWidth);
                AppendSegments(spike.Waveform, current.Length, x, y, vertices);
                spikeCounts[spike.Channel]++;
            }

            if (vertices.Count > 0)
                gpu.Accumulate(vertices);
        }

        /// <summary>
        /// Draws one channel's density at the cursor, with zero, the threshold, and the duration and count labeled,
        /// and the time and amplitude under the pointer. Call it with the window's GL context current, as it is
        /// while a frame is drawn.
        /// </summary>
        /// <remarks>
        /// The amplitudes drawn are given each time, so a view beside a plot can take the plot's and change with it.
        /// The threshold's line can be dragged, and the value it is dropped at is returned for the owner to detect
        /// at.
        /// </remarks>
        /// <param name="channel">The channel.</param>
        /// <param name="size">The size of the view, in pixels.</param>
        /// <param name="color">The color the waveforms are drawn in, packed as <c>0xAABBGGRR</c>.</param>
        /// <param name="center">The value at the middle of the view, in the waveforms' units.</param>
        /// <param name="range">The amplitude the view's height spans, in the waveforms' units.</param>
        /// <param name="threshold">The threshold spikes were detected at, in the waveforms' units, or null for none.</param>
        /// <returns>The threshold dropped this frame, or null while none was.</returns>
        public unsafe double? Draw(int channel, Vector2 size, uint color, double center, double range, double? threshold = null)
        {
            Flush();
            var min = ImGui.GetCursorScreenPos();
            var w = (int)size.X;
            var h = (int)size.Y;
            if (w <= 0 || h <= 0 || range <= 0)
                return null;

            ImGui.InvisibleButton("##spikeView", new Vector2(w, h));
            var id = ImGui.GetItemID();
            var hovered = ImGui.IsItemHovered();
            var active = ImGui.IsItemActive();
            var mouse = ImGui.GetMousePos();

            var draw = ImGui.GetWindowDrawList();
            var max = min + new Vector2(w, h);
            if (!gpu.Ready())
            {
                draw.AddText(min + new Vector2(4, 4), ImGuiPalette.VibrantCoral, $"Spikes cannot be drawn: {gpu.Error}");
                return null;
            }

            if ((uint)channel < (uint)channels)
            {
                var texture = gpu.Display(id, channel, w, h, (float)center, (float)range, spikeCounts[channel], color);
                if (texture != 0)
                    draw.AddImage(new ImTextureRef(null, (ulong)texture), min, max);
            }

            float? HeightOf(double value)
            {
                var fraction = 0.5 - (value - center) / range;
                return fraction > 0 && fraction < 1 ? MathF.Floor(min.Y + (float)fraction * h) + 0.5f : null;
            }

            double ValueAt(float y) => center + range * (0.5 - (Math.Max(min.Y, Math.Min(max.Y, y)) - min.Y) / h);

            double? dropped = null;
            var shownThreshold = threshold;
            var grabbed = false;
            if (threshold is double t)
            {
                var near = hovered && HeightOf(t) is float lineY && Math.Abs(mouse.Y - lineY) <= ThresholdGrab;
                if (near && ImGui.IsItemActivated())
                {
                    draggingThreshold = true;
                    thresholdMoved = false;
                }

                if (draggingThreshold)
                {
                    thresholdMoved |= ImGui.IsMouseDragging(ImGuiMouseButton.Left);
                    shownThreshold = thresholdMoved ? ValueAt(mouse.Y) : t;
                    if (!active)
                    {
                        draggingThreshold = false;
                        if (thresholdMoved)
                            dropped = shownThreshold;
                    }
                }

                grabbed = near || draggingThreshold;
                if (grabbed)
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
            }
            else
                draggingThreshold = false;

            if (HeightOf(0) is float zero)
                draw.AddLine(new Vector2(min.X, zero), new Vector2(max.X, zero), ImGuiPalette.WithAlpha(ImGuiPalette.Grey0x80, 0x80));
            if (shownThreshold is double shown && HeightOf(shown) is float y)
            {
                var lineColor = grabbed ? ImGuiPalette.White : ImGuiPalette.Grey0xCC;
                for (var x = min.X; x < max.X; x += 8)
                    draw.AddLine(new Vector2(x, y), new Vector2(Math.Min(x + 4, max.X), y), lineColor);
                if (draggingThreshold)
                {
                    var framePad = ImGui.GetStyle().FramePadding;
                    PlotText.Framed(draw, new Vector2(min.X + 2 * framePad.X, y - ImGui.GetTextLineHeight() - 2 * framePad.Y),
                        WithUnit($"{PlotText.Significant(shown)}"));
                }
            }
            PixelRect.Outline(draw, min, max, ImGuiWaveformPanel.ColGraticule, ImGuiWaveformPanel.GraticuleWeight);

            var text = ImGuiPalette.Grey0x99;
            var pad = new Vector2(4, 2);
            draw.AddText(min + pad, text, $"{SpikeCount(channel)} spikes");
            if (hovered && !draggingThreshold)
            {
                // NB: the samples are a tile's columns apart, the first half a column in, as the display shader
                // reads them.
                var column = (mouse.X - min.X) / w * current.TileWidth;
                var milliseconds = 1000 * ((column - 0.5) / Oversample - current.Delay) / current.SampleRate;
                var reading = $"{milliseconds:0.00} ms   {WithUnit($"{ValueAt(mouse.Y):0.0}")}";
                var corner = max - ImGui.CalcTextSize(reading) - 2 * ImGui.GetStyle().FramePadding;
                PlotText.Framed(draw, corner, reading);
            }
            else
            {
                var duration = $"{1000 * current.Length / current.SampleRate:0.##} ms";
                draw.AddText(max - pad - ImGui.CalcTextSize(duration), text, duration);
            }

            return dropped;
        }

        string WithUnit(string value) => string.IsNullOrEmpty(Unit) ? value : $"{value} {Unit}";

        // NB: the amplitude below which the bins are spaced near evenly in the amplitude itself.
        internal static float Knee(float span) => span / 256;

        internal static int TileWidth(int length) => (length - 1) * Oversample + 1;

        /// <summary>
        /// The row an amplitude falls in, from the bottom of a channel's tile, kept to the tile. The same mapping as
        /// the shaders' <c>binOf</c>.
        /// </summary>
        internal static float BinOf(float value, float span, float knee)
        {
            var v = Math.Max(-span, Math.Min(span, value));
            var bin = (float)((Asinh(v / knee) / Asinh(span / knee) * 0.5 + 0.5) * Bins);
            return Math.Max(0.5f, Math.Min(Bins - 0.5f, bin));
        }

        /// <summary>
        /// The amplitude at a row of a channel's tile, the inverse of <see cref="BinOf"/>.
        /// </summary>
        internal static float ValueOf(float bin, float span, float knee) =>
            (float)(knee * Math.Sinh((bin / Bins - 0.5) * 2 * Asinh(span / knee)));

        static double Asinh(double x) => Math.Log(x + Math.Sqrt(x * x + 1));

        /// <summary>
        /// The bottom left of a channel's tile in the atlas, in pixels.
        /// </summary>
        internal static (float X, float Y) TileOrigin(int channel, int tilesPerRow, int tileWidth) =>
            (channel % tilesPerRow * tileWidth, channel / tilesPerRow * Bins);

        /// <summary>
        /// Appends the line segments of a waveform for the accumulating shader: each end as its x in the atlas, its
        /// tile's first row, and its value.
        /// </summary>
        internal static void AppendSegments(float[] waveform, int length, float tileX, float tileY, List<float> into)
        {
            for (int i = 0; i < length - 1; i++)
            {
                into.Add(tileX + 0.5f + i * Oversample);
                into.Add(tileY);
                into.Add(waveform[i]);
                into.Add(tileX + 0.5f + (i + 1) * Oversample);
                into.Add(tileY);
                into.Add(waveform[i + 1]);
            }
        }

        /// <summary>
        /// The GL objects the densities live in and are drawn with, made the first time they are needed in the
        /// window's context.
        /// </summary>
        sealed class Gpu
        {
            // NB: a whole number of segments, so a chunk never splits a line. The buffer is made this size once, so
            // however many waveforms pile up between frames, no upload is larger.
            const int ChunkFloats = 65536 * 3;

            // NB: the atlas holds half floats, which count whole numbers only up to 2048, where adding one rounds
            // back down, so no pixel holds more.
            const float MostCounted = 2048;

            readonly ImGuiSpikeView view;
            readonly Dictionary<uint, (int Texture, int Width, int Height)> targets = new();
            readonly float[] chunk = new float[ChunkFloats];
            int atlas;
            int atlasWidth;
            int atlasHeight;
            int atlasFramebuffer;
            int targetFramebuffer;
            int accumulate;
            int display;
            int vertexArray;
            int vertexBuffer;
            int quadArray;
            bool tried;

            public Gpu(ImGuiSpikeView view) => this.view = view;

            public string? Error { get; private set; }

            public int TilesPerRow { get; private set; } = 1;

            public int TileWidth { get; private set; }

            public bool Ready()
            {
                if (!tried)
                {
                    // NB: GL's entry points are loaded with a context, and a call made without one faults the
                    // process past any catch, so nothing is tried until one is current.
                    if (GraphicsContext.CurrentContext is null)
                    {
                        Error = "no OpenGL context is current";
                        return false;
                    }

                    tried = true;
                    Error = null;
                    try { Build(); }
                    catch (Exception ex) { Error = ex.Message; }
                }

                return Error is null;
            }

            void Build()
            {
                var common = Source("SpikeView.Common.glsl");
                accumulate = Link(Source("SpikeView.Accumulate.vert"), Source("SpikeView.Accumulate.frag"), common);
                display = Link(Source("SpikeView.Display.vert"), Source("SpikeView.Display.frag"), common);
                BuildAtlas(view.current.TileWidth);

                targetFramebuffer = GL.GenFramebuffer();
                vertexArray = GL.GenVertexArray();
                vertexBuffer = GL.GenBuffer();
                GL.BindVertexArray(vertexArray);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffer);
                GL.BufferData(BufferTarget.ArrayBuffer, ChunkFloats * sizeof(float), IntPtr.Zero, BufferUsageHint.StreamDraw);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
                GL.BindVertexArray(0);
                GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

                // NB: the quad comes from gl_VertexID alone, and a driver may still fetch an enabled attribute the
                // program does not read, so it is drawn with none enabled.
                quadArray = GL.GenVertexArray();
            }

            /// <summary>
            /// Makes the atlas over for tiles of a new width, empty.
            /// </summary>
            public void Resize(int tileWidth)
            {
                try { BuildAtlas(tileWidth); }
                catch (Exception ex) { Error = ex.Message; }
            }

            void BuildAtlas(int tileWidth)
            {
                if (atlas != 0)
                    GL.DeleteTexture(atlas);

                var largest = GL.GetInteger(GetPName.MaxTextureSize);
                TileWidth = tileWidth;
                TilesPerRow = Math.Max(1, Math.Min(view.channels, largest / tileWidth));
                atlasWidth = TilesPerRow * tileWidth;
                atlasHeight = (view.channels + TilesPerRow - 1) / TilesPerRow * Bins;
                if (atlasHeight > largest)
                    throw new InvalidOperationException($"{view.channels} channels need more texture than the GPU allows.");

                // NB: linear, so the view reads between bins smoothly when zoomed in past them.
                atlas = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, atlas);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R16f, atlasWidth, atlasHeight, 0,
                    PixelFormat.Red, PixelType.Float, IntPtr.Zero);

                if (atlasFramebuffer == 0)
                    atlasFramebuffer = GL.GenFramebuffer();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, atlasFramebuffer);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2D, atlas, 0);
                var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                if (status != FramebufferErrorCode.FramebufferComplete)
                    throw new InvalidOperationException($"framebuffer {status}");

                ClearAtlas();
            }

            public void ClearAtlas()
            {
                var viewport = Viewport();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, atlasFramebuffer);
                GL.Viewport(0, 0, atlasWidth, atlasHeight);
                GL.Disable(EnableCap.ScissorTest);
                GL.ClearColor(0, 0, 0, 0);
                GL.Clear(ClearBufferMask.ColorBufferBit);
                Restore(viewport);
            }

            // NB: added to what is there, one for each line passing through a pixel.
            public void Accumulate(List<float> points)
            {
                var viewport = Viewport();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, atlasFramebuffer);
                GL.Viewport(0, 0, atlasWidth, atlasHeight);
                GL.Disable(EnableCap.ScissorTest);
                GL.Disable(EnableCap.DepthTest);
                GL.Disable(EnableCap.CullFace);
                GL.Enable(EnableCap.Blend);
                GL.BlendEquation(BlendEquationMode.FuncAdd);
                GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);

                GL.UseProgram(accumulate);
                SetMapping(accumulate);
                GL.Uniform2(GL.GetUniformLocation(accumulate, "atlasSize"), (float)atlasWidth, atlasHeight);
                GL.BindVertexArray(vertexArray);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBuffer);
                for (int first = 0; first < points.Count; first += ChunkFloats)
                {
                    var count = Math.Min(ChunkFloats, points.Count - first);
                    points.CopyTo(first, chunk, 0, count);
                    GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, count * sizeof(float), chunk);
                    GL.DrawArrays(PrimitiveType.Lines, 0, count / 3);
                }

                GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
                GL.Disable(EnableCap.Blend);
                Restore(viewport);
            }

            /// <summary>
            /// Draws a channel's density, under the given range, into a texture of the view's size, kept for the
            /// view by its ImGui ID, and returns the texture.
            /// </summary>
            public int Display(uint id, int channel, int width, int height, float center, float range, long spikes, uint color)
            {
                if (!targets.TryGetValue(id, out var target) || target.Width != width || target.Height != height)
                {
                    if (target.Texture != 0)
                        GL.DeleteTexture(target.Texture);
                    target = (Texture(width, height), width, height);
                    targets[id] = target;
                }

                var viewport = Viewport();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFramebuffer);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2D, target.Texture, 0);
                if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                {
                    Restore(viewport);
                    return 0;
                }

                GL.Viewport(0, 0, width, height);
                GL.Disable(EnableCap.ScissorTest);
                GL.Disable(EnableCap.Blend);
                GL.Disable(EnableCap.DepthTest);
                GL.Disable(EnableCap.CullFace);
                GL.ClearColor(0, 0, 0, 0);
                GL.Clear(ClearBufferMask.ColorBufferBit);

                var (tileX, tileY) = TileOrigin(channel, TilesPerRow, TileWidth);
                var c = ImGui.ColorConvertU32ToFloat4(color);
                GL.UseProgram(display);
                SetMapping(display);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, atlas);
                GL.Uniform1(GL.GetUniformLocation(display, "atlas"), 0);
                GL.Uniform2(GL.GetUniformLocation(display, "atlasSize"), (float)atlasWidth, atlasHeight);
                GL.Uniform2(GL.GetUniformLocation(display, "tile"), tileX, tileY);
                GL.Uniform2(GL.GetUniformLocation(display, "tileSize"), (float)TileWidth, Bins);
                GL.Uniform2(GL.GetUniformLocation(display, "viewSize"), (float)width, height);
                GL.Uniform1(GL.GetUniformLocation(display, "center"), center);
                GL.Uniform1(GL.GetUniformLocation(display, "range"), range);
                GL.Uniform1(GL.GetUniformLocation(display, "spikes"), Math.Min(spikes, MostCounted));
                GL.Uniform4(GL.GetUniformLocation(display, "color"), c.X, c.Y, c.Z, c.W);
                GL.BindVertexArray(quadArray);
                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

                Restore(viewport);
                return target.Texture;
            }

            void SetMapping(int program)
            {
                GL.Uniform1(GL.GetUniformLocation(program, "knee"), view.knee);
                GL.Uniform1(GL.GetUniformLocation(program, "span"), view.span);
                GL.Uniform1(GL.GetUniformLocation(program, "bins"), Bins);
            }

            static int Texture(int width, int height)
            {
                var texture = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, texture);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
                return texture;
            }

            static int[] Viewport()
            {
                var viewport = new int[4];
                GL.GetInteger(GetPName.Viewport, viewport);
                return viewport;
            }

            static void Restore(int[] viewport)
            {
                GL.BindVertexArray(0);
                GL.UseProgram(0);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GL.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
            }

            static string Source(string name)
            {
                using var stream = typeof(ImGuiSpikeView).Assembly.GetManifestResourceStream($"OpenEphys.EphysVisualization.Design.Shaders.{name}")
                    ?? throw new InvalidOperationException($"The shader {name} is missing.");
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }

            static int Link(string vertex, string fragment, string common)
            {
                const string Version = "#version 330 core\n";
                var vertexShader = Compile(ShaderType.VertexShader, Version + common + vertex);
                var fragmentShader = Compile(ShaderType.FragmentShader, Version + common + fragment);
                var program = GL.CreateProgram();
                GL.AttachShader(program, vertexShader);
                GL.AttachShader(program, fragmentShader);
                GL.LinkProgram(program);
                GL.DeleteShader(vertexShader);
                GL.DeleteShader(fragmentShader);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
                if (linked == 0)
                    throw new InvalidOperationException(GL.GetProgramInfoLog(program));
                return program;
            }

            static int Compile(ShaderType type, string source)
            {
                var shader = GL.CreateShader(type);
                GL.ShaderSource(shader, source);
                GL.CompileShader(shader);
                GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
                if (compiled == 0)
                    throw new InvalidOperationException(GL.GetShaderInfoLog(shader));
                return shader;
            }
        }
    }
}
