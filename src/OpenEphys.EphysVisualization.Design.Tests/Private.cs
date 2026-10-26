using System;
using System.Collections.Generic;
using System.Reflection;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    /// <summary>
    /// Reads and calls the library's private members by name.
    /// </summary>
    /// <remarks>
    /// So that the library's internal surface stays what its own code is meant to use, rather than growing for the
    /// tests. A renamed member fails the tests that use it with its name, rather than the build.
    /// </remarks>
    static class Private
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static T Get<T>(object target, string name) => (T)Value(target.GetType(), target, name)!;

        public static T Get<T>(Type type, string name) => (T)Value(type, null, name)!;

        public static T Call<T>(Type type, string name, params object?[] args) => (T)Method(type, name).Invoke(null, args)!;

        /// <remarks>
        /// The method's out arguments are written back into <paramref name="args"/>.
        /// </remarks>
        public static T Call<T>(object target, string name, object?[] args) =>
            (T)Method(target.GetType(), name).Invoke(target, args)!;

        static object? Value(Type type, object? target, string name)
        {
            if (type.GetField(name, Any) is { } field)
                return field.GetValue(target);
            if (type.GetProperty(name, Any) is { } property)
                return property.GetValue(target);
            throw new MissingMemberException(type.FullName, name);
        }

        static MethodInfo Method(Type type, string name) =>
            type.GetMethod(name, Any) ?? throw new MissingMethodException(type.FullName, name);
    }

    /// <summary>
    /// The panel's state the tests check, which the panel keeps private.
    /// </summary>
    static class PanelState
    {
        public static PixelAxis Axis(this ImGuiWaveformPanel panel) => Private.Get<PixelAxis>(panel, "Axis");

        public static ImGuiWaveformPanel.RowLayout Rows(this ImGuiWaveformPanel panel) =>
            Private.Get<ImGuiWaveformPanel.RowLayout>(panel, "lastLayout");

        public static (double Center, double Span) AmplitudeView(this ImGuiWaveformPanel panel) =>
            Private.Get<(double, double)>(panel, "AmplitudeView");

        public static IReadOnlyList<long> CursorPositions(this ImGuiWaveformPanel panel) =>
            Private.Get<List<long>>(Private.Get<WaveformCursors>(panel, "cursors"), "positions");
    }
}
