# OpenEphys.EphysVisualization

OpenEphys.EphysVisualization is a [Bonsai](https://bonsai-rx.org/) library of visualization tools for electrophysiology, maintained by [Open Ephys](https://open-ephys.org/). It covers both the display of recorded signals and the interfaces used to configure acquisition instruments, and draws them with Dear ImGui and OpenGL.

^^^
![LfpViewer heatmap of a NeuropixelsV2 recording](~/images/lfp-viewer/npx2_overview.png){#fig-overview .centered width="900"}
^^^ A Neuropixels V2 cortical recording displayed using an [LfpViewer](articles/lfp-viewer.md#heatmap) heatmap.

## Documentation

- [LFP Viewer](articles/lfp-viewer.md) describes the LfpViewer node and everything its display can do.
- The [API reference](xref:OpenEphys.EphysVisualization.Design) lists every public type, for building on the library.

## What's in the package

- [LfpViewer](xref:OpenEphys.EphysVisualization.Design.LfpViewer) shows a sequence of channel-by-sample matrices as a live multichannel waveform display, with a heatmap, cursors, clipping detection, and a record of recent signal that can be paused and moved back through.
- [WaveformScope\<TSource\>](xref:OpenEphys.EphysVisualization.Design.WaveformScope%601) and [WaveformScopeVisualizer](xref:OpenEphys.EphysVisualization.Design.WaveformScopeVisualizer) are the base other packages build waveform viewers for their own data on, as the ProbeScope nodes in OpenEphys.Onix1 do.
- [ImGuiProbeSelector](xref:OpenEphys.EphysVisualization.Design.ImGuiProbeSelector) draws a minimap and a zoomable view of a probe described by a [ProbeInterface](https://probeinterface.readthedocs.io/) file, with contact selection.
- [ImGuiWidgets](xref:OpenEphys.EphysVisualization.Design.ImGuiWidgets), [ImGuiPalette](xref:OpenEphys.EphysVisualization.Design.ImGuiPalette) and [ImGuiColormapRangeSlider](xref:OpenEphys.EphysVisualization.Design.ImGuiColormapRangeSlider) give Open Ephys instrument configuration interfaces their shared widgets and look, and [ImGuiGLControl](xref:OpenEphys.EphysVisualization.Design.WinForms.ImGuiGLControl) hosts an ImGui context in a WinForms control.

## Requirements

The library runs on Windows with OpenGL 3.3 or later. A package that draws with ImGui alongside it must reference the same version of Hexa.NET.ImGui, since ImGui types appear in this library's API.

## Acknowledgments

The LFP viewer grew out of the [WaveformVisualizer](https://github.com/bonsai-rx/ephys/blob/main/src/Bonsai.Ephys.Design/WaveformVisualizer.cs) in Bonsai.Ephys.
