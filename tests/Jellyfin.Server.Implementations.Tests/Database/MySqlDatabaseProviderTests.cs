using System;
using System.Collections.ObjectModel;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.DbConfiguration;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Database;

public sealed class MySqlDatabaseProviderTests
{
    [Theory]
    [InlineData("Server=localhost;Database=old;", "newdb", "Server=localhost;Database=newdb;")]
    [InlineData("Server=localhost;Port=3306;User=root;Database=test;", "newdb", "Server=localhost;Port=3306;User=root;Database=newdb;")]
    [InlineData("Server=localhost;Port=3306;Database=old;CharSet=utf8;", "newdb", "Server=localhost;Port=3306;Database=newdb;CharSet=utf8;")]
    [InlineData("Server=localhost;", "newdb", "Server=localhost;Database=newdb;")]
    public void ApplySchema_ChangesDatabaseName(string input, string schema, string expected)
    {
        var method = typeof(MySqlDatabaseProvider).GetMethod(
            "ApplySchema",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        var result = method!.Invoke(null, [input, schema]);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetOption_ReadsValueFromOptions()
    {
        var opts = new Collection<CustomDatabaseOption>
        {
            new() { Key = "server", Value = "192.168.1.1" },
            new() { Key = "port", Value = "3307" },
        };

        var server = MySqlDatabaseProvider.GetOption(opts, "server", e => e, () => "localhost");
        Assert.Equal("192.168.1.1", server);

        var port = MySqlDatabaseProvider.GetOption(opts, "port", e => e, () => "3306");
        Assert.Equal("3307", port);
    }

    [Fact]
    public void GetOption_ReturnsDefaultWhenMissing()
    {
        var opts = new Collection<CustomDatabaseOption>();

        var result = MySqlDatabaseProvider.GetOption<string?>(opts, "nonexistent", e => e, () => null);
        Assert.Null(result);
    }

    [Fact]
    public void GetOption_ReturnsDefaultWhenNullOptions()
    {
        var result = MySqlDatabaseProvider.GetOption(null, "anything", e => e, () => "defaultVal");
        Assert.Equal("defaultVal", result);
    }
}
