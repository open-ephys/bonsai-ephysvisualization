# LFP Viewer

The [LfpViewer](xref:OpenEphys.EphysVisualization.Design.LfpViewer) node shows a sequence of matrices as a live multichannel waveform display ([](#fig-overview)). Each matrix is one block of samples, with one row per channel and one column per sample, of any element type and one value per element. The node passes its input through unchanged, so it can sit anywhere in a workflow, and its display opens like any other visualizer.

^^^
![LfpViewer heatmap of a NeuropixelsV2 recording](~/images/lfp-viewer/npx2_overview.png){#fig-overview}
^^^ All 384 channels of a NeuropixelsV2 recording are fitted to the window and shown as a heatmap.

## Getting started

This workflow plays back a NeuropixelsV2 recording, saved by the Open Ephys GUI as 384 channels of signed 16-bit samples at 30 kHz, with MatrixReader, and loops it with Repeat. To use it, set the reader's Path to a recording of your own, and its ChannelCount, SampleRate and Depth to match it.

:::workflow
![A Neuropixels recording played from a file into LfpViewer](~/workflows/npx2-playback.bonsai)
:::

The LfpViewer's settings describe the samples it is given:

- SampleRate is left at its default of 30000, the recording's rate.
- Scale is 0.195, the microvolts each count stands for in this recording, so the amplitudes shown are in microvolts, though they are still labeled a.u., since the viewer cannot know that.
- Offset is left at 0, since the samples are signed and centered on zero.
- SaturationLow and SaturationHigh are left empty, so a channel is marked as clipping when a sample reaches −32768 or 32767, the limits of 16-bit samples.

For a signal that needs no hardware or recording, see [Single channel](#single-channel).

The viewer opens like any visualizer, from the node, and can be closed and reopened during a run. To open it when the workflow starts, including when Bonsai runs without the editor (`--no-editor`), place Bonsai's VisualizerWindow operator after the node; its properties set the window's position and size.

Every control has a tooltip saying what it does and naming its keys and gestures. Labels name only the control and, in parentheses, its unit.

## Settings

The display's settings are properties of the node, saved with the workflow. The viewer reads and writes them directly, so a change made in the viewer is saved with the workflow, including when it is saved during a run, and a change made in the property grid shows in the viewer at once.

### Input

| Property | Description |
|---|---|
| [SampleRate](xref:OpenEphys.EphysVisualization.Design.LfpViewer.SampleRate) | Each channel is sampled at this rate, in Hz, which sets the time axis. |
| [Scale](xref:OpenEphys.EphysVisualization.Design.LfpViewer.Scale), [Offset](xref:OpenEphys.EphysVisualization.Design.LfpViewer.Offset) | What is drawn is (sample − Offset) × Scale, as when turning ADC counts into microvolts. The viewer cannot know what unit that gives, so amplitudes are labeled a.u., arbitrary units. |
| [SaturationLow](xref:OpenEphys.EphysVisualization.Design.LfpViewer.SaturationLow), [SaturationHigh](xref:OpenEphys.EphysVisualization.Design.LfpViewer.SaturationHigh) | A channel is marked as clipping when a sample, before Offset and Scale are applied, reaches either of these values (see [Clipping](#clipping)). When they are left empty, the limits of the input's integer type are used, such as 0 and 65535 for unsigned 16-bit samples, and floating point input is not checked. An ADC that does not fill its type, such as a 10-bit ADC in 16-bit samples, never reaches those limits, so for it they need to be set. |
| [HistorySize](xref:OpenEphys.EphysVisualization.Design.LfpViewer.HistorySize) | This is the memory, in megabytes, given to the record of recent signal that a paused view is redrawn from. How many seconds that holds depends on the channel count and sample rate, and is shown in the control panel. |

### Display

Each of these is also a control in the viewer (see [Control panel](#control-panel)).

| Property | Control |
|---|---|
| [Timebase](xref:OpenEphys.EphysVisualization.Design.WaveformScope.Timebase) | Timebase |
| [RangeAmplitude](xref:OpenEphys.EphysVisualization.Design.WaveformScope.RangeAmplitude) | Range |
| [ChannelHeight](xref:OpenEphys.EphysVisualization.Design.WaveformScope.ChannelHeight), [FitChannels](xref:OpenEphys.EphysVisualization.Design.WaveformScope.FitChannels) | Chan. Height and Fit |
| [Palette](xref:OpenEphys.EphysVisualization.Design.WaveformScope.Palette), [CustomColor](xref:OpenEphys.EphysVisualization.Design.WaveformScope.CustomColor) | Palette |
| [ColorGroupingEnabled](xref:OpenEphys.EphysVisualization.Design.LfpViewer.ColorGroupingEnabled), [ColorGrouping](xref:OpenEphys.EphysVisualization.Design.LfpViewer.ColorGrouping) | Color Groups |
| [ShowHeatmap](xref:OpenEphys.EphysVisualization.Design.WaveformScope.ShowHeatmap), [ColorThreshold](xref:OpenEphys.EphysVisualization.Design.WaveformScope.ColorThreshold) | Heatmap and Color |
| [ShowClipping](xref:OpenEphys.EphysVisualization.Design.WaveformScope.ShowClipping) | Clipping |
| [HiddenChannels](xref:OpenEphys.EphysVisualization.Design.WaveformScope.HiddenChannels) | The channel labels |
| [Maximized](xref:OpenEphys.EphysVisualization.Design.WaveformScope.Maximized) | The maximize button |

## Keys and mouse

The viewer is meant to be driven while watching the data, without reaching for the control panel. The left hand rests on W, A, S and D, with Shift, Ctrl and Alt beside them, and the right hand on the mouse, as in a game. Between them they move through channels and time, zoom, and set the gain in quick strokes, so that a burst on one channel can be found, magnified and measured in a few seconds.

The keys and the mouse follow one scheme:

- W and S, and the wheel, move through the channels. A and D, and the wheel with Shift, move through time, which a paused view can do, since a live one follows the incoming data.
- Shift makes a move larger, most of a screen rather than a channel or a division. On the mouse it turns the wheel sideways, as in other applications.
- Ctrl zooms: with W and S, or the wheel, it sets the channel height; with A and D, or the wheel with Shift as well, it sets the timebase.
- Alt sets the gain, the range, with W and S, the wheel, or a drag.
- Up and right magnify.

Every key and gesture acts on a setting that a control in the control panel shows, so its effect is always in view and can also be set by hand there.

[](#fig-stacked-gestures) runs through them in this order:

1. The wheel, then W and S, move through the channels.
2. Shift+W and Shift+S move through the channels most of a screen at a time.
3. Ctrl+Wheel, then Ctrl+W and Ctrl+S, set the channel height.
4. Alt+Wheel, then Alt+W and Alt+S, set the gain.
5. Space pauses the display. Then Shift+Wheel, A and D, and Shift+A and Shift+D move back and forth through time.
6. A middle-drag moves through the channels and time at once.
7. A Ctrl+drag zooms in on a pair of large spikes.

^^^
<video id="fig-stacked-gestures" controls preload="metadata" src="../images/lfp-viewer/npx2_stacked-view-gestures.webm"></video>
^^^ The keys and mouse are used on a NeuropixelsV2 recording, with the keys and buttons pressed shown as they are used.

### Keys

Each key has a single owner in the window, so a key does the same thing wherever the pointer is within it. Keys are ignored while typing in a text field. Keys reach the viewer under the pointer, whichever window has the keyboard.

| Key | Action | When |
|---|---|---|
| Space | Pause or resume | Always |
| W / S | Scroll the channels up or down by one channel | Always |
| Shift+W / Shift+S | Scroll the channels by most of a screen | Always |
| Ctrl+W / Ctrl+S | Make the channels taller or shorter, to the next multiple of 5 px | No channel expanded |
| Alt+W / Alt+S | Narrow or widen the range, through its presets | Always |
| A / D | Move back or forward in time by one division | Paused |
| Shift+A / Shift+D | Move back or forward in time by most of a screen | Paused |
| Ctrl+A / Ctrl+D | Lengthen or shorten the timebase, through its presets, about the middle of the plot | Always |
| C | Show or hide the cursors | Always |
| H | Switch between traces and the heatmap | More than one channel |
| F | Turn Fit on or off (see Chan. Height) | More than one channel |
| M | Give the plot the whole window, or restore the layout | Always |
| Q / E | Select the previous or next channel, skipping hidden channels. With a channel expanded, expand that one instead | Always |
| Escape | Clear the selection | Always |
| X | Expand the selected channel, selecting the first shown channel in view if none is, or collapse the expanded one, returning to the view from before the expand, moved only as far as brings the channel into view | More than one channel |

### Mouse

Dragged zooms and gain round to three significant figures, and an axis answers only past a few pixels, so a drag along one barely moves the other.

| Gesture | Where | Action | When |
|---|---|---|---|
| Wheel | Plot or channel labels | Scroll the channels, or on an expanded channel move up or down its amplitudes by a division per notch | Always |
| Shift+Wheel, or a sideways swipe or tilt | Plot or channel labels | Move back or forward in time by a division per notch | Paused |
| Ctrl+Wheel, or a pinch | Plot | Make the channels taller or shorter, to the next multiple of 5 px, keeping the channel under the pointer in place. On an expanded channel, magnify or shrink its amplitude, keeping the value under the pointer in place | Always |
| Ctrl+Shift+Wheel | Plot | Shorten or lengthen the timebase through its presets, keeping the time under the pointer in place while paused | Always |
| Alt+Wheel | Plot | Narrow or widen the range through its presets | Always |
| Ctrl+Drag | Plot | Drag up or down to make the channels taller or shorter, keeping the channel pressed on in place, or on an expanded channel to magnify or shrink its amplitude, keeping the value pressed on in place. Drag right or left to shorten or lengthen the timebase, keeping the time pressed on in place. A diagonal drag does both | Paused |
| Alt+Drag (vertical) | Plot | Narrow the range upward, widen it downward | Always |
| Middle-drag | Plot or channel labels | Grab the plot and move it with the pointer: through the channels, or an expanded channel's amplitudes, and through time while paused | Always |
| Double-click | A channel | Expand the channel to fill the plot, or collapse it, returning to the view from before the expand, moved only as far as brings the channel into view | More than one channel |
| Click / Drag (vertical) | Channel labels | Show the channel, or every channel dragged across, whether hidden or not. A click also selects the channel | No channel expanded |
| Shift+Click / Shift+Drag (vertical) | Channel labels | Hide the channel, or every channel dragged across | No channel expanded |
| Drag | Time scroll bar below the plot | Move through time | Paused |
| Drag (horizontal) | A cursor line | Move the cursor, snapping to the pixel under the pointer | Cursors shown |
| Click | A channel's trace, away from the cursor lines | Select the channel, which the cursors read | No channel expanded |
| Hover | A channel | Show its readout, and, when no channel is expanded, highlight its label and outline its trace, or in the heatmap draw grey lines just above and below its row | Always |

## Control panel

The control panel is the row of controls under the plot ([](#fig-control-panel)). Each control shows and sets one of the node's display settings.

^^^
![LfpViewer control panel with numbered controls](~/images/lfp-viewer/control-strip.png){#fig-control-panel}
^^^ The control panel is shown in trace view, with color groups of 10 channels. Its numbers match those in the table below.

| Control | Description |
|---|---|
| 1. Collapse arrow | The arrow folds the control panel away, or brings it back. |
| 2. Timebase | Timebase sets the seconds the plot spans. Type a value, or pick a preset from the arrow. While the display is paused, only the presets the record can fill are offered. |
| 3. Chan. Height | Chan. Height sets the height of each channel's row, in pixels. It is never less than the fit height, at which every channel just fills the plot's height (or 1 px, if there are more channels than pixels), and anything that would set it lower sets the fit height instead. The − and + buttons, like Ctrl+Wheel, go to the next lower or higher multiple of 5 px. Fit keeps the height at the fit height, following the window as it is resized, and disables the box; F turns it on or off. Ctrl+Wheel and Ctrl+Drag still change the height, which turns Fit off. |
| 4. Range | Range sets the amplitude one channel's row spans, in the units the node's Scale and Offset give. Type a value, or pick a preset from the arrow. The presets step 1, 2.5, 5, 10 through the decades around the current range, so they suit data of any size. |
| 4. Color | In the heatmap, Color replaces Range. Its bar shows both halves of the colormap, positive above and negative below, and runs from 0 up to the range box at its right end, which works as Range does in trace view. Drag anywhere on the bar to set the threshold, whose value follows the handle: magnitudes at or below it are drawn black, and those above it keep their usual color. |
| 5. Palette | Palette sets the colors of the traces: Open Ephys GUI, or Custom, which adds a swatch for picking the color its shades are built from. |
| 6. Color Groups | When Color Groups is checked, consecutive channels share a color in groups of the given size ([](#fig-color-groups)). The − and + buttons step the size. A size at least the channel count draws every channel in one color. |
| 7. Heatmap, Cursors, Clipping | These are three checkboxes in one column. Heatmap, key H, draws each channel as a row of color instead of a trace (see [Heatmap](#heatmap)). Cursors, key C, shows or hides the cursors and the cursor table. Clipping, on by default, shows or hides the clipping warning: CLIPPING and the flashing labels (see [Clipping](#clipping)). |
| 8. Pause | The Pause button pauses or resumes the display, as Space does, and is highlighted while the display is paused. |
| 9. Record bar and Record Length | The record of recent signal, which a paused view is redrawn from, is a ring buffer whose size is set by the node's HistorySize. The bar shows how full it is, and Record Length gives the seconds it holds out of the seconds it can hold. Once it is full, each new sample replaces the oldest, so it always holds the most recent signal, and the bar stays full. |

^^^
![Traces in color groups](~/images/lfp-viewer/npx2_color-groups.png){#fig-color-groups}
^^^ Channels share colors in groups of 4, on the Open Ephys GUI palette. At 10 px a row, only every 2nd channel is labeled.

## Reading the plot

The plot shows each channel as a trace, or as a row of color in the heatmap, against time. Everything drawn in and around it is described below, grouped by what it tells you.

### Live and paused

When the display is live, new samples are written across the plot from left to right, in sweeps. At short timebases, where a sweep would be too fast to follow, each frame instead shows the last whole sweep, so the traces appear complete at once, as on a triggered scope (see [How the display is drawn](#how-the-display-is-drawn)). When the display is paused, it holds still, and can be moved back through the record of recent signal ([](#fig-paused)).

^^^
![A paused view with hidden and selected channels](~/images/lfp-viewer/npx2_plot-paused.png){#fig-paused}
^^^ The view is paused and scrolled back through the record. Channels 6, 11, 12, 19 and 20 are hidden, so their labels are dimmed and their rows are empty. Channel 14 is selected, so its label is boxed and a band lies behind its row.

| Element | Description |
|---|---|
| Time labels | The time labels run above the plot. For live data, they show the time since the start of the sweep. When the display is paused, they show the time before the moment of pausing. |
| Sweep cursor | The sweep cursor is a yellow line. For live data, it marks where new samples are being written. When the display is paused, it marks where the display stopped. It is hidden while a sweep is too fast to follow, when the last whole sweep is shown instead (see [How the display is drawn](#how-the-display-is-drawn)). |
| Frozen tail shading | When the display is paused, the part of the plot right of the sweep cursor, which shows the previous sweep, is darkened. |
| Time scroll bar | The time scroll bar below the plot shows where the screen sits in the record. While the display is paused, it can be dragged to move through time. |
| Divisions | Dim dotted lines divide the plot into ten divisions of time across it and, on an expanded channel, ten divisions of amplitude down it. |

### Channels

Each channel has a row, labeled on its left. A channel can be hidden, selected for the cursors to read, and read under the pointer.

| Element | Description |
|---|---|
| Channel labels | The channel labels run down the left of the plot. The labels of hidden channels are dimmed ([](#fig-paused)). The hovered channel's label is filled in its trace color, and the label of the channel the cursors read is filled the same way and boxed. On rows shorter than the text, only every 2nd, 5th or 10th channel is labeled, but the hovered channel and the one the cursors read always are. |
| Readout | While the pointer is over a channel, the readout in the lower right shows the channel, the time, and the amplitude at the pointer. The channel is left out while a channel is expanded, since it is named in the top left. |

### Heatmap

The heatmap draws each channel's row as a strip of color instead of a trace, which brings out activity spread across many channels. H, or the Heatmap checkbox, switches to it and back.

<!-- VIDEO: images/lfp-viewer/npx2_fit-heatmap.webm — fitting and the heatmap (see TODO item 3). -->

| Element | Description |
|---|---|
| Colors | At each pixel of a row, the color shows whichever of the minimum and maximum lies further from zero, on the icefire colormap. The colormap is black at zero and brightens toward blue below and yellow above, with its ends at plus and minus the range. |
| Threshold | Magnitudes at or below the threshold are black, and those above it keep their usual color, so that only larger activity shows (see Color in [Control panel](#control-panel)). |
| Rows | Rows sit exactly where the traces do, so switching views keeps every channel in place. Double-click a row to read it as a trace, and double-click again to return. |
| Offsets | A channel's offset shows as solid color, so remove it upstream, or with Offset where it is the same on every channel. |

### Expanded channel

An expanded channel fills the plot on its own, with an amplitude axis, so that one channel can be studied closely. Double-click a channel, or select it and press X, to expand it, and do the same to return.

<!-- VIDEO: images/lfp-viewer/npx2_selection-expansion.webm — selecting and expanding channels (see TODO item 3). -->

| Element | Description |
|---|---|
| Amplitude labels | Labels down the left of the plot give the amplitude of each division. An expanded channel can be magnified and moved up and down its amplitudes (see [Mouse](#mouse)), so that, for example, a peak can be dragged to the middle of the plot and magnified there, but it never shows more than the range. The zoom belongs to the view, not the channel, so expanding another channel, collapsing, or changing the range starts it over. |
| Channel name | The channel name in the top left of the plot gives the channel, the amplitude per division and the time per division. |

### Clipping

A channel clips when a sample reaches the node's saturation limits (see [Input](#input)). While any channel has clipped within the last second of data, CLIPPING flashes in the plot's top right corner, beside the maximize button, and that channel's label flashes red in step with it, written even where labels are thinned out ([](#fig-clipping)). Clicking CLIPPING brings the first clipping channel to the middle of the plot, or expands it where a channel is expanded ([](#fig-clipping-expanded)).

Each channel's second starts again whenever it clips, and counts data, not wall time, so the marks also stay while no data arrives. When the display is paused, the marks stay as they were when it was paused and stop flashing, so the channels that were clipping stay marked, steadily, while the paused view is studied. Hidden channels raise no warning, so hiding a channel that is always clipping silences it, and the Clipping checkbox silences them all.

The examples below come from a live NeuropixelsV1 probe left floating, out of tissue, with its gain set to 3000 so that its channels would clip. That is not a sensible way to run a probe; it was done only to show the warning.

^^^
![The clipping warning](~/images/lfp-viewer/npx1_clipping.png){#fig-clipping}
^^^ CLIPPING shows in the plot's top right, and the labels of channels 32 and 37, which clipped, flash red.

^^^
![An expanded clipping channel](~/images/lfp-viewer/npx1_clipping-expanded.png){#fig-clipping-expanded}
^^^ With a clipping channel expanded, its trace runs flat along the top of the ADC's range while it clips.

### Maximize

The maximize button, in the plot's top right corner and faint until hovered, gives the plot the whole window, hiding the control panel. While the plot is maximized, the button shows the restore glyph, which brings the layout back. M does the same.

## Cursors

Up to four cursors measure time and amplitude on the selected channel ([](#fig-cursors)).

^^^
![Cursors on an expanded channel](~/images/lfp-viewer/npx2_cursors.png){#fig-cursors}
^^^ Four cursors sit on successive spikes of channel 131, each measured from C0, the reference. Their dimensions are drawn above, and the cursor table below.

| Element | Description |
|---|---|
| C0 to C3 | There can be up to four cursors, C0 to C3. C0 is always present while cursors are shown, and the others are added from the cursor table. |
| Reference | The reference is the cursor the others are measured from, C0 to begin with. It is drawn as a solid line with a filled time label, where the others are dotted and outlined. Clicking a cursor's time label makes it the reference. The reference cannot be removed. |
| Time label | Each cursor's time label sits in the row of time labels, over any it would collide with, and gives the cursor's name and time. Clicking it makes that cursor the reference; where labels overlap, the one on top takes the click. |
| Selected channel | The cursors read the selected channel, which is chosen by clicking its trace or label, or with Q and E. Its label is boxed, and a faint band lies behind its row; in the heatmap, which would cover the band, white lines lie just above and below the row instead. With none selected, turning the cursors on or pressing X selects the first shown channel in view. Escape clears the selection, as does hiding the channel. |
| Markers and values | Where each cursor crosses the selected channel's trace, markers show the maximum above and the minimum below of what the pixel there is drawn from, or the single sample when the pixel holds one. In the heatmap there is no marker, and the one value the color shows is labeled level with the middle of the row. |
| Dimensions | Near the top of the plot, each cursor has a dimension line from the reference to it, with an arrowhead at the cursor. The line is broken around the time between them and its inverse in Hz, which sit on a dark fill so that they read over lines and graticules. |
| Cursor table | The cursor table, in the lower left, lists the selected channel and, for each cursor, its time, the time from the reference (negative before it) and its inverse, the minimum and maximum, and dy min and dy max. At one sample per pixel, and always in the heatmap, it shows a single value y and its difference from the reference, dy, instead. |
| dy min, dy max | These bound how far the signal at a cursor may differ from the signal at the reference. A cursor reads what its pixel is drawn from, which spans the signal's minimum to maximum over the samples it covers, so the true difference between the two cursors lies somewhere from dy min (this cursor's minimum less the reference's maximum) to dy max (this cursor's maximum less the reference's minimum). The range narrows as zooming in puts fewer samples in each pixel. At one sample per pixel the two are equal, and the table shows the single value y and its difference dy. |
| + Add cursor | This button adds a cursor in the middle of the screen. It is disabled at four cursors. |
| Home | Home brings the cursors to the middle of the screen, keeping their spacing, or spreads them evenly if they span more than the screen. |
| C0 to C3 buttons | While the display is paused, each cursor's button in the cursor table centers the screen on that cursor, which reaches a cursor that is off the screen. The reference's button is filled, as its time label is. |
| x | The x button removes that cursor. |

## How the display is drawn

This section describes what the viewer does with the signal it is given. None of it changes the signal itself.

| Step | Description |
|---|---|
| History | Every sample is kept in a record of recent signal, sized by HistorySize, from which a paused view is redrawn. The record is not written while the display is paused, and is cleared when it resumes. |
| Decimation | Each channel's sweep is divided into columns, as many as the widest monitor has pixels across, each keeping the minimum and maximum of the samples it covers. At short timebases, where a sweep spans fewer samples than that, a column holds a single sample. |
| Drawing | Columns are combined to one per pixel of plot width, again as minimum and maximum, and each pixel column is drawn as a span between them. Which columns a pixel combines is counted from the start of the sweep, not the edge of the screen, and a paused view pans a whole pixel at a time, so panning never regroups them, and a pixel, and a cursor reading it, keeps its value. Each pixel's span reaches to its neighbor's so that steep steps stay joined. Where there are fewer columns than pixels, the samples are interpolated into a line. The traces are drawn into an image, and while the display is live, only the columns the sweep crossed since the last frame are redrawn. The hovered or expanded channel's outline is drawn over the image every frame. |
| Fast sweeps | When a sweep lasts 10 refreshes of the display or fewer (timebases of 100 ms or less on a 100 Hz display, about 167 ms at 60 Hz), each frame shows the last whole sweep rather than the one in progress. Drawn in so few steps, a sweep would read as jumps rather than motion, and at the fastest it would alias against the frame rate. |

## Single channel

With one channel, the viewer always shows it expanded, as a double-click would, and it cannot be collapsed ([](#fig-single-channel)). Chan. Height, Fit and Heatmap arrange a stack of channels, so they are disabled, and their keys do nothing. Everything else, the expanded channel's amplitude zoom included, works as usual.

The workflow below makes one: it generates a sine wave with FunctionGenerator, as 8-bit samples at 44.1 kHz, so it needs no hardware or recording.

:::workflow
![Function generator into LfpViewer](~/workflows/function-generator.bonsai)
:::

^^^
![A single expanded channel](~/images/lfp-viewer/sine-gen_expanded.png){#fig-single-channel}
^^^ The generated sine wave's one channel is shown expanded, with Chan. Height, Fit and Heatmap disabled.
