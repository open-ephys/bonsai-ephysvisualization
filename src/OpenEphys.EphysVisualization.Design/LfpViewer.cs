using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using Bonsai;
using OpenCV.Net;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Provides a sink that shows a sequence of channel-by-sample matrices as a live multi-channel waveform viewer.
    /// </summary>
    /// <remarks>
    /// Each row of a matrix is a channel and each column a sample. What is drawn is
    /// <c>(sample - <see cref="Offset"/>) * <see cref="Scale"/></c>, in whatever unit that makes it, while
    /// clipping is checked on the samples as they arrive.
    /// </remarks>
    [TypeVisualizer(typeof(LfpViewerVisualizer))]
    [Description("Displays a sequence of channel-by-sample matrices as live waveforms.")]
    public class LfpViewer : WaveformScope
    {
        /// <summary>
        /// Gets or sets the rate, in Hz, at which each channel is sampled.
        /// </summary>
        [DefaultValue(30000)]
        [Description("The rate, in Hz, at which each channel is sampled.")]
        public int SampleRate { get; set; } = 30000;

        /// <summary>
        /// Gets or sets the factor each sample is multiplied by, after subtracting the offset, to give the value
        /// displayed.
        /// </summary>
        [DefaultValue(1.0)]
        [Description("The factor each sample is multiplied by, after subtracting the offset, to give the value " +
            "displayed.")]
        public double Scale { get; set; } = 1;

        /// <summary>
        /// Gets or sets the value subtracted from each sample before it is scaled.
        /// </summary>
        [DefaultValue(0.0)]
        [Description("The value subtracted from each sample before it is scaled.")]
        public double Offset { get; set; }

        /// <summary>
        /// Gets or sets the sample value at or below which a channel is marked as clipping, in the units the
        /// samples arrive in.
        /// </summary>
        /// <remarks>
        /// Left empty, the least value of an integer input's depth is used, and a floating point input is not
        /// checked. An ADC that does not fill its integer type never reaches that value, and needs it set.
        /// </remarks>
        [Description("The sample value at or below which a channel is marked as clipping, before offset and " +
            "scale. Left empty, the least value of an integer input's type is used.")]
        public double? SaturationLow { get; set; }

        /// <summary>
        /// Gets or sets the sample value at or above which a channel is marked as clipping, in the units the
        /// samples arrive in.
        /// </summary>
        /// <remarks>
        /// Left empty, the greatest value of an integer input's depth is used, and a floating point input is not
        /// checked. An ADC that does not fill its integer type never reaches that value, and needs it set.
        /// </remarks>
        [Description("The sample value at or above which a channel is marked as clipping, before offset and " +
            "scale. Left empty, the greatest value of an integer input's type is used.")]
        public double? SaturationHigh { get; set; }

        /// <summary>
        /// Gets or sets the memory, in megabytes, given to the data retained behind the display.
        /// </summary>
        /// <remarks>
        /// How many seconds that holds depends on the channel count and sample rate, neither of which is known
        /// until data arrives, and is shown in the viewer.
        /// </remarks>
        [DefaultValue(512)]
        [Description("The memory, in megabytes, given to the data retained behind the display, which a paused " +
            "view can be moved back through. The seconds it holds are shown in the viewer.")]
        public int HistorySize { get; set; } = 512;

        /// <summary>
        /// Gets or sets a value specifying whether neighboring channels share a color in groups of
        /// <see cref="ColorGrouping"/>.
        /// </summary>
        [DefaultValue(false)]
        [Description("Specifies whether neighboring channels share a color in groups.")]
        public bool ColorGroupingEnabled { get; set; }

        /// <summary>
        /// Gets or sets the number of neighboring channels sharing a color when grouping is enabled.
        /// </summary>
        [DefaultValue(1)]
        [Description("The number of neighboring channels sharing a color when grouping is enabled.")]
        public int ColorGrouping { get; set; } = 1;

        internal long HistoryBytes => (long)Math.Max(1, HistorySize) * 1024 * 1024;

        /// <inheritdoc/>
        public override Expression Build(IEnumerable<Expression> arguments)
        {
            var source = arguments.First();
            if (source.Type != typeof(IObservable<Mat>))
                throw new InvalidOperationException($"{GetType().Name} displays a sequence of {nameof(Mat)}.");

            // NB: through a call of its own rather than returning the source, which the editor would take for the
            // upstream node's output and offer that node's visualizers instead of this one's.
            return Expression.Call(Expression.Constant(this, typeof(LfpViewer)), nameof(Process), null, source);
        }

        IObservable<Mat> Process(IObservable<Mat> source) => source;
    }
}
