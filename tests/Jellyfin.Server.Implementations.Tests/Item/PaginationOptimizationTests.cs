using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Item;

#pragma warning disable SA1402, SA1649
public static class PaginationOptimizer
{
    public static IQueryable<T> ApplyPaging<T>(IQueryable<T> query, int? startIndex, int? limit)
    {
        if (startIndex.HasValue && startIndex.Value > 0)
        {
            query = query.Skip(startIndex.Value);
        }

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return query;
    }
}
#pragma warning restore SA1402, SA1649

public class PaginationOptimizationTests
{
    private static IQueryable<int> CreateTestSequence(int count)
    {
        return Enumerable.Range(0, count).AsQueryable();
    }

    [Fact]
    public void ApplyPaging_SkipAndTake_ReturnsCorrectPage()
    {
        var items = CreateTestSequence(30);
        var result = PaginationOptimizer.ApplyPaging(items, 5, 10).ToList();
        Assert.Equal(10, result.Count);
        Assert.Equal(5, result[0]);
        Assert.Equal(14, result[^1]);
    }

    [Fact]
    public void ApplyPaging_OnlySkip_NoLimit_ReturnsRemaining()
    {
        var items = CreateTestSequence(30);
        var result = PaginationOptimizer.ApplyPaging(items, 5, null).ToList();
        Assert.Equal(25, result.Count);
        Assert.Equal(5, result[0]);
        Assert.Equal(29, result[^1]);
    }

    [Fact]
    public void ApplyPaging_NoPaging_ReturnsAll()
    {
        var items = CreateTestSequence(30);
        var result = PaginationOptimizer.ApplyPaging(items, null, null).ToList();
        Assert.Equal(30, result.Count);
        Assert.Equal(0, result[0]);
        Assert.Equal(29, result[^1]);
    }

    [Fact]
    public void ApplyPaging_NegativeSkip_TreatsAsZero()
    {
        var items = CreateTestSequence(30);
        var result = PaginationOptimizer.ApplyPaging(items, -1, 10).ToList();
        Assert.Equal(10, result.Count);
        Assert.Equal(0, result[0]);
        Assert.Equal(9, result[^1]);
    }

    [Fact]
    public void ApplyPaging_OnlyTake_LimitedSet()
    {
        var items = CreateTestSequence(30);
        var result = PaginationOptimizer.ApplyPaging(items, null, 5).ToList();
        Assert.Equal(5, result.Count);
        Assert.Equal(0, result[0]);
        Assert.Equal(4, result[^1]);
    }

    [Fact]
    public void ApplyPaging_ExcessiveTake_NoError()
    {
        var items = CreateTestSequence(30);
        var result = PaginationOptimizer.ApplyPaging(items, 0, 100).ToList();
        Assert.Equal(30, result.Count);
    }

    [Fact]
    public void ApplyPaging_ExcessiveSkip_ReturnsEmpty()
    {
        var items = CreateTestSequence(30);
        var result = PaginationOptimizer.ApplyPaging(items, 50, 10).ToList();
        Assert.Empty(result);
    }
}

