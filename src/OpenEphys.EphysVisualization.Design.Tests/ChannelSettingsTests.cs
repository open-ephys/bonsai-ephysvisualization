using System.Linq;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class ChannelSettingsTests
    {
        [Fact]
        public void WithGroup_MovesTheChannelsIntoTheGroup_KeepingTheGroupsOthers()
        {
            var channels = ChannelSettings.WithGroup(null, new[] { 3, 1 }, 2);
            channels = ChannelSettings.WithGroup(channels, new[] { 5 }, 2);

            Assert.Equal(new[] { (1, 2), (3, 2), (5, 2) }, Groups(channels));
        }

        [Fact]
        public void WithGroup_Replacing_MovesTheGroupsOthersToTheDefaultGroup()
        {
            var channels = ChannelSettings.WithGroup(null, new[] { 1, 3 }, 2);
            channels = ChannelSettings.WithGroup(channels, new[] { 4 }, 1);

            channels = ChannelSettings.WithGroup(channels, new[] { 5 }, 2, replace: true);

            Assert.Equal(new[] { (4, 1), (5, 2) }, Groups(channels));
        }

        [Fact]
        public void WithGroup_ToTheDefaultGroup_KeepsAHiddenChannel_AndDropsTheRest()
        {
            var channels = new[]
            {
                new ChannelSettings { Index = 2, Hidden = true, Group = 3 },
                new ChannelSettings { Index = 7, Group = 3 },
            };

            channels = ChannelSettings.WithGroup(channels, new[] { 2, 7 }, 0);

            var only = Assert.Single(channels!);
            Assert.Equal((2, true, 0), (only.Index, only.Hidden, only.Group));
            Assert.Null(ChannelSettings.WithGroup(new[] { new ChannelSettings { Index = 7, Group = 3 } }, new[] { 7 }, 0));
        }

        static (int, int)[] Groups(ChannelSettings[]? channels) =>
            channels!.Select(channel => (channel.Index, channel.Group)).ToArray();
    }
}
