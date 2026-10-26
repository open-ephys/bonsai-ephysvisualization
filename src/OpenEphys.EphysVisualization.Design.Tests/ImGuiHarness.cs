using System;
using System.Collections.Generic;
using System.Numerics;
using Hexa.NET.ImGui;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    // NB: ImGui's current context is one global, so tests that draw run one at a time.
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class ImGuiCollection
    {
        public const string Name = "ImGui";
    }

    /// <summary>
    /// An ImGui context with no window or renderer, which runs frames and takes input as the visualizer's does.
    /// </summary>
    sealed unsafe class ImGuiHarness : IDisposable
    {
        public static readonly Vector2 DisplaySize = new(1200, 800);

        readonly ImGuiContextPtr context;

        public ImGuiHarness()
        {
            context = ImGui.CreateContext(null);
            ImGui.SetCurrentContext(context);
            var io = ImGui.GetIO();
            io.IniFilename = null;

            // NB: claimed so that ImGui builds the font atlas itself, as it does for a renderer that uploads it,
            // where without one it requires the atlas built beforehand. Nothing here uploads it.
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            io.DisplaySize = DisplaySize;
            io.DeltaTime = 1 / 60f;
        }

        /// <summary>
        /// Runs one frame, drawing into a window that fills the display as the visualizer's does.
        /// </summary>
        public void Frame(Action draw)
        {
            ImGui.NewFrame();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(DisplaySize);
            ImGui.Begin("##test",
                ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoNavInputs);
            ImGui.BeginChild("##plot", Vector2.Zero);
            draw();
            ImGui.EndChild();
            ImGui.End();
            ImGui.Render();
        }

        public void Frames(int count, Action draw)
        {
            for (int i = 0; i < count; i++)
                Frame(draw);
        }

        // NB: one input event a frame, as a person gives them, so each is seen on a frame of its own.
        public void MoveMouse(Vector2 position, Action draw)
        {
            ImGui.GetIO().AddMousePosEvent(position.X, position.Y);
            Frame(draw);
        }

        public void Button(ImGuiMouseButton button, bool down, Action draw)
        {
            ImGui.GetIO().AddMouseButtonEvent((int)button, down);
            Frame(draw);
        }

        public void Click(ImGuiMouseButton button, Action draw)
        {
            Button(button, true, draw);
            Button(button, false, draw);
        }

        public void Key(ImGuiKey key, Action draw)
        {
            ImGui.GetIO().AddKeyEvent(key, true);
            Frame(draw);
            ImGui.GetIO().AddKeyEvent(key, false);
            Frame(draw);
        }

        /// <summary>
        /// Presses <paramref name="key"/> while <paramref name="modifier"/>, such as <see cref="ImGuiKey.ModShift"/>,
        /// is held.
        /// </summary>
        public void Key(ImGuiKey modifier, ImGuiKey key, Action draw)
        {
            ImGui.GetIO().AddKeyEvent(modifier, true);
            Frame(draw);
            Key(key, draw);
            ImGui.GetIO().AddKeyEvent(modifier, false);
            Frame(draw);
        }

        /// <summary>
        /// Holds <paramref name="key"/> down or lets it go, such as a modifier held through a mouse gesture.
        /// </summary>
        public void Hold(ImGuiKey key, bool down, Action draw)
        {
            ImGui.GetIO().AddKeyEvent(key, down);
            Frame(draw);
        }

        /// <summary>
        /// The filled rectangles of <paramref name="color"/> in the last frame's draw data, which ImGui writes as
        /// four corners in turn.
        /// </summary>
        public static List<Rect> Rects(uint color)
        {
            var rects = new List<Rect>();
            var data = ImGui.GetDrawData();
            for (int l = 0; l < data.CmdListsCount; l++)
            {
                var vertices = data.CmdLists[l].VtxBuffer;
                for (int i = 0; i + 3 < vertices.Size; i++)
                {
                    var (a, b, c, d) = (vertices[i], vertices[i + 1], vertices[i + 2], vertices[i + 3]);
                    if (a.Col != color || b.Col != color || c.Col != color || d.Col != color)
                        continue;
                    if (a.Pos.Y != b.Pos.Y || b.Pos.X != c.Pos.X || c.Pos.Y != d.Pos.Y || d.Pos.X != a.Pos.X)
                        continue;

                    rects.Add(new Rect(a.Pos, c.Pos));
                    i += 3;
                }
            }
            return rects;
        }

        public void Dispose()
        {
            ImGui.DestroyContext(context);
        }
    }

    readonly record struct Rect(Vector2 Min, Vector2 Max)
    {
        public Vector2 Center => (Min + Max) / 2;
    }
}
