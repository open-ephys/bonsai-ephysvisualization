using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Xml.Serialization;
using OpenCV.Net;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    public class WaveformScopeTests
    {
        [Fact]
        public void Build_PassesEachMatrixThroughUnchanged()
        {
            var source = Expression.Parameter(typeof(IObservable<Mat>));
            var body = new WaveformScope().Build(new[] { source });
            var process = Expression.Lambda<Func<IObservable<Mat>, IObservable<Mat>>>(body, source).Compile();

            using var first = new Mat(2, 3, Depth.S16, 1);
            using var second = new Mat(2, 5, Depth.F32, 1);
            var passed = process(new[] { first, second }.ToObservable()).ToArray().Wait();

            Assert.Equal(new[] { first, second }, passed);
        }

        [Fact]
        public void Build_RejectsASequenceOfAnotherType()
        {
            var source = Expression.Parameter(typeof(IObservable<int>));

            Assert.Throws<InvalidOperationException>(() => new WaveformScope().Build(new[] { source }));
        }

        [Fact]
        public void ChannelSettings_AreSavedAsOneElementEach_LeavingOutTheirDefaults()
        {
            var scope = new WaveformScope
            {
                Channels = new[]
                {
                    new ChannelSettings { Index = 3, Hidden = true },
                    new ChannelSettings { Index = 9, Group = 2 },
                },
            };
            var serializer = new XmlSerializer(typeof(WaveformScope));
            using var writer = new StringWriter();

            serializer.Serialize(writer, scope);
            var xml = writer.ToString();
            var read = (WaveformScope)serializer.Deserialize(new StringReader(xml));

            Assert.Contains("<Channel Index=\"3\" Hidden=\"true\" />", xml);
            Assert.Contains("<Channel Index=\"9\" Group=\"2\" />", xml);
            var channels = Assert.IsType<ChannelSettings[]>(read.Channels);
            Assert.Equal(new[] { (3, true, 0), (9, false, 2) },
                channels.Select(channel => (channel.Index, channel.Hidden, channel.Group)));
        }
    }
}
