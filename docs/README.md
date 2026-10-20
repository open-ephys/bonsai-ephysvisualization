# OpenEphys.EphysVisualization

GUI elements for electrophysiology in [Bonsai](https://bonsai-rx.org/), drawn with Dear ImGui and OpenGL: live multichannel waveform plotting, views of probes described by [ProbeInterface](https://probeinterface.readthedocs.io/) files with contact selection, and the shared widgets and look of Open Ephys instrument configuration interfaces.

## Contents

- **LfpViewer**: a sink that shows a sequence of channel-by-sample matrices as a live multichannel waveform display, with a heatmap view, cursors, clipping detection, and a record of recent signal that can be paused and scrolled back through.
- **Waveform viewer base**: `WaveformScope<TSource>` and `WaveformScopeVisualizer`, on which other packages build viewers for their own data, such as the ProbeScope nodes in OpenEphys.Onix1.
- **Probe view**: `ImGuiProbeSelector`, a minimap and zoomable view of a probe's contacts, with drag selection.
- **Shared widgets and host**: `ImGuiWidgets` (tooltips, label and value rows, confirmation modals), `ImGuiPalette`, `ImGuiColormapRangeSlider`, and `ImGuiGLControl`, the WinForms control that hosts an ImGui context.

## Requirements

Windows, with OpenGL 3.3 or later. A package that draws with ImGui alongside this one must reference the same version of Hexa.NET.ImGui, since ImGui types appear in this package's API.

## Acknowledgments

The waveform viewer grew out of the [`WaveformVisualizer`](https://github.com/bonsai-rx/ephys/blob/main/src/Bonsai.Ephys.Design/WaveformVisualizer.cs) in Bonsai.Ephys.
