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
        /// Gets or sets the group the channel belongs to: 0, the default group every channel starts in, or a group it
        /// was put in, numbered from 1.
        /// </summary>
        [XmlAttribute]
        [DefaultValue(0)]
        [Description("The group the channel belongs to: 0, the default group, or a group numbered from 1.")]
        public int Group { get; set; }

        bool IsDefault => !Hidden && Group == 0;

        internal static int[] HiddenOf(ChannelSettings[]? channels) =>
            channels?.Where(channel => channel.Hidden).Select(channel => channel.Index).ToArray() ?? Array.Empty<int>();

        internal static ChannelSettings[]? WithHidden(ChannelSettings[]? channels, IEnumerable<int> hidden)
        {
            var hiding = new HashSet<int>(hidden);
            return Rebuilt(channels, hiding, (index, old) => new ChannelSettings
            {
                Index = index,
                Hidden = hiding.Contains(index),
                Group = old?.Group ?? 0,
            });
        }

        /// <summary>
        /// Returns channel settings with channels moved into a group.
        /// </summary>
        /// <param name="channels">The settings to start from, or null for every channel at its defaults.</param>
        /// <param name="moved">The channels to move.</param>
        /// <param name="group">The group to move them into, or 0 to move them back to the default group.</param>
        /// <param name="replace">Whether the group's other channels move back to the default group.</param>
        /// <returns>
        /// A new array, in channel order, of the channels whose settings differ from the defaults, or null if none
        /// does.
        /// </returns>
        public static ChannelSettings[]? WithGroup(ChannelSettings[]? channels, IEnumerable<int> moved, int group,
            bool replace = false)
        {
            var moving = new HashSet<int>(moved);
            return Rebuilt(channels, moving, (index, old) => new ChannelSettings
            {
                Index = index,
                Hidden = old?.Hidden ?? false,
                Group = moving.Contains(index) ? group
                    : replace && old?.Group == group ? 0
                    : old?.Group ?? 0,
            });
        }

        // NB: a new array rather than the entries changed in place, since the panel works its masks out again only
        // when the array is replaced.
        static ChannelSettings[]? Rebuilt(ChannelSettings[]? channels, IEnumerable<int> changed,
            Func<int, ChannelSettings?, ChannelSettings> rebuild)
        {
            var byIndex = channels?.ToDictionary(channel => channel.Index) ?? new Dictionary<int, ChannelSettings>();
            var result = byIndex.Keys.Union(changed)
                .Select(index => rebuild(index, byIndex.TryGetValue(index, out var old) ? old : null))
                .Where(channel => !channel.IsDefault)
                .OrderBy(channel => channel.Index)
                .ToArray();
            return result.Length > 0 ? result : null;
        }
    }
}
