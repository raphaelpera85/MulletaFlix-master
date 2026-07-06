using System;
using System.Collections.Generic;
using System.Linq;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.Entities;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Database;

public sealed class QueryHelperTests
{
    [Fact]
    public void WhereOneOrMany_WithMultipleIds_ReturnsAllMatchingItems()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        var query = new[]
        {
            new BaseItemEntity { Id = firstId, Type = "Movie" },
            new BaseItemEntity { Id = secondId, Type = "Movie" },
            new BaseItemEntity { Id = thirdId, Type = "Movie" }
        }.AsQueryable();

        var result = query
            .WhereOneOrMany(new List<Guid> { firstId, secondId }, item => item.Id)
            .Select(item => item.Id)
            .ToArray();

        Assert.Equal([firstId, secondId], result);
    }
}
