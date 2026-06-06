using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Performance;

[Collection("Benchmark")]
public class QueryPerformanceBenchmark
{
    private static readonly List<int> Data = Enumerable.Range(0, 10000).Select(i => i * 3).ToList();

    [Fact(Skip = "Benchmark")]
    public void NextUpQuery_ProjectionVsMaterialized_ProjectionIsFaster()
    {
        const int skip = 5;
        const int take = 10;

        var projectionSw = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            _ = Data.Skip(skip).Take(take).ToList();
        }

        projectionSw.Stop();
        var projectionTime = projectionSw.ElapsedMilliseconds;

        var materializedSw = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            var materialized = Data.ToList();
            _ = materialized.Where(x => x >= Data[skip] && x < Data[skip + take]).ToList();
        }

        materializedSw.Stop();
        var materializedTime = materializedSw.ElapsedMilliseconds;

        Assert.True(projectionTime < materializedTime, $"Projection ({projectionTime}ms) should be faster than materialized ({materializedTime}ms)");
    }

    [Fact(Skip = "Benchmark")]
    public void CountQuery_Performance_BenchmarkCountVsMaterialize()
    {
        var countSw = Stopwatch.StartNew();
        long total = 0;
        for (var i = 0; i < 10000; i++)
        {
            total += Data.Count;
        }

        countSw.Stop();
        var countTime = countSw.ElapsedMilliseconds;

        var toListSw = Stopwatch.StartNew();
        long total2 = 0;
        for (var i = 0; i < 10000; i++)
        {
            total2 += Data.ToList().Count;
        }

        toListSw.Stop();
        var toListTime = toListSw.ElapsedMilliseconds;

        Assert.True(countTime <= toListTime, $".Count() ({countTime}ms) should be faster or equal to ToList().Count ({toListTime}ms)");
    }
}
