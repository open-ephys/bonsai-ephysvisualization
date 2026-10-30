using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Represents the settings of one channel, kept only for a channel whose settings differ from the defaults.
    /// </summary>
    public class ChannelSettings
    {
        /// <summary>
        /// Gets or sets the index of the channel.
        /// </summary>
        [XmlAttribute]
        [Description("The index of the channel.")]
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets a value specifying whether the channel is not drawn.
        /// </summary>
        [XmlAttribute]
        [DefaultValue(false)]
        [Description("Specifies whether the channel is not drawn.")]
        public bool Hidden { get; set; }

        /// <summary>
        /// Gets or sets the group the channel belongs to, or 0 for none.
        /// </summary>
        [XmlAttribute]
        [DefaultValue(0)]
        [Description("The group the channel belongs to, or 0 for none.")]
        public int Group { get; set; }

        bool IsDefault => !Hidden && Group == 0;

        internal static int[] HiddenOf(ChannelSettings[]? channels) =>
            channels?.Where(channel => channel.Hidden).Select(channel => channel.Index).ToArray() ?? Array.Empty<int>();

        // NB: a new array rather than the entries changed in place, since the panel works its masks out again only
        // when the array is replaced.
        internal static ChannelSettings[]? WithHidden(ChannelSettings[]? channels, IEnumerable<int> hidden)
        {
            var hiding = new HashSet<int>(hidden);
            var indices = (channels?.Select(channel => channel.Index) ?? Enumerable.Empty<int>()).Union(hiding);
            var byIndex = channels?.ToDictionary(channel => channel.Index) ?? new Dictionary<int, ChannelSettings>();
            var result = indices
                .Select(index => new ChannelSettings
                {
                    Index = index,
                    Hidden = hiding.Contains(index),
                    Group = byIndex.TryGetValue(index, out var old) ? old.Group : 0,
                })
                .Where(channel => !channel.IsDefault)
                .OrderBy(channel => channel.Index)
                .ToArray();
            return result.Length > 0 ? result : null;
        }
    }
}
