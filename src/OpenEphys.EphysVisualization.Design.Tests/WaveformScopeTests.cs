using System;
using System.Linq.Expressions;
using System.Reactive.Linq;
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
    }
}
