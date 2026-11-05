using System;
using System.IO;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// GL objects made the first time they are needed, in whichever context is current then, with the shaders they
    /// draw with.
    /// </summary>
    internal abstract class GLResources
    {
        bool tried;

        /// <summary>
        /// Why nothing can be drawn with these, or null.
        /// </summary>
        public string? Error { get; protected set; }

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

        /// <summary>
        /// Makes the GL objects, throwing with the reason if one cannot be made.
        /// </summary>
        protected abstract void Build();

        protected static string Source(string name)
        {
            using var stream = typeof(GLResources).Assembly.GetManifestResourceStream($"OpenEphys.EphysVisualization.Design.Shaders.{name}")
                ?? throw new InvalidOperationException($"The shader {name} is missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        protected static int Link(string vertex, string fragment, string common)
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
