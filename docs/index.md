# OpenEphys.EphysVisualization

OpenEphys.EphysVisualization is a [Bonsai](https://bonsai-rx.org/) library of visualization tools for electrophysiology, maintained by [Open Ephys](https://open-ephys.org/). It covers both the display of recorded signals and the interfaces used to configure acquisition instruments, and draws them with Dear ImGui and OpenGL.

^^^
![WaveformScope heatmap of a NeuropixelsV2 recording](~/images/waveform-scope/npx2_overview.png){#fig-overview .centered width="900"}
^^^ A Neuropixels V2 cortical recording displayed using a [WaveformScope](articles/waveform-scope.md#heatmap) heatmap.

## Documentation

- [Waveform Scope](articles/waveform-scope.md) describes the WaveformScope node and everything its display can do.
- [Probe Selector](articles/probe-selector.md) describes the probe selector, its gestures and rulers, and how to host it.
- The [API reference](xref:OpenEphys.EphysVisualization.Design) lists every public type, for building on the library.

## What's in the package

- [WaveformScope](xref:OpenEphys.EphysVisualization.Design.WaveformScope) shows a sequence of channel-by-sample matrices as a live multichannel waveform display, with a heatmap, cursors, clipping detection, and a record of recent signal that can be paused and moved back through.
- [WaveformScopeBuilder\<TSource\>](xref:OpenEphys.EphysVisualization.Design.WaveformScopeBuilder%601) and [WaveformScopeVisualizerBase](xref:OpenEphys.EphysVisualization.Design.WaveformScopeVisualizerBase) are the base other packages build waveform viewers for their own data on.
- [ImGuiProbeSelector](xref:OpenEphys.EphysVisualization.Design.ImGuiProbeSelector) draws a minimap and a zoomable view of a probe described by a [ProbeInterface](https://probeinterface.readthedocs.io/) file, with contact selection.
- [ImGuiWidgets](xref:OpenEphys.EphysVisualization.Design.ImGuiWidgets), [ImGuiPalette](xref:OpenEphys.EphysVisualization.Design.ImGuiPalette) and [ImGuiColormapRangeSlider](xref:OpenEphys.EphysVisualization.Design.ImGuiColormapRangeSlider) give Open Ephys instrument configuration interfaces their shared widgets and look, and [ImGuiGLControl](xref:OpenEphys.EphysVisualization.Design.WinForms.ImGuiGLControl) hosts an ImGui context in a WinForms control.

## Requirements

The library runs on Windows with OpenGL 3.3 or later. A package that draws with ImGui alongside it must reference the same version of Hexa.NET.ImGui, since ImGui types appear in this library's API.

## Acknowledgments

The waveform scope grew out of the [WaveformVisualizer](https://github.com/bonsai-rx/ephys/blob/main/src/Bonsai.Ephys.Design/WaveformVisualizer.cs) in Bonsai.Ephys.
