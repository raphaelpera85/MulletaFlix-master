using System;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MulletaFlix.Server.Implementations.Billing;

public static class DomainSchemaInitializer
{
    public static async Task EnsureDomainTablesAsync(DbContext dbContext, string schemaName, CancellationToken ct)
    {
        var providerName = dbContext.Database.ProviderName ?? string.Empty;
        if (!providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase) &&
            !providerName.Contains("MariaDb", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tables = schemaName switch
        {
            "mulletaflix_movies" => new[]
            {
                ("Movies", MoviesTableSql()),
                ("MovieMetadata", MovieMetadataTableSql()),
                ("MovieUserData", MovieUserDataTableSql())
            },
            "mulletaflix_series" => new[]
            {
                ("Series", SeriesTableSql()),
                ("Seasons", SeasonsTableSql()),
                ("Episodes", EpisodesTableSql()),
                ("SeriesUserData", SeriesUserDataTableSql())
            },
            "mulletaflix_channels" => new[]
            {
                ("Channels", ChannelsTableSql()),
                ("Programs", ProgramsTableSql())
            },
            "mulletaflix_books" => new[]
            {
                ("Books", BooksTableSql()),
                ("BookUserData", BookUserDataTableSql())
            },
            "mulletaflix_system" => Array.Empty<(string TableName, string Sql)>(), // Uses mullettaflix_users schema via SystemDbContext
            _ => Array.Empty<(string TableName, string Sql)>()
        };

        foreach (var (tableName, sql) in tables)
        {
            if (await TableExistsAsync(dbContext, schemaName, tableName, ct).ConfigureAwait(false))
            {
                continue;
            }

            await dbContext.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false);
        }
    }

    private static async Task<bool> TableExistsAsync(DbContext dbContext, string schemaName, string tableName, CancellationToken ct)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = false;

        try
        {
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                openedHere = true;
            }

            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = @schemaName
                  AND table_name = @tableName
                """;

            AddParameter(command, "@schemaName", schemaName);
            AddParameter(command, "@tableName", tableName);

            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt64(result, CultureInfo.InvariantCulture) > 0;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string MoviesTableSql() => """
        CREATE TABLE `Movies` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `BaseItemId` char(36) NOT NULL,
            `Name` varchar(500) NULL,
            `Overview` text NULL,
            `ProductionYear` int NULL,
            `Runtime` double NULL,
            `CommunityRating` float NULL,
            `IsActive` tinyint(1) NOT NULL DEFAULT 1,
            `CreatedAt` datetime(6) NOT NULL,
            `UpdatedAt` datetime(6) NOT NULL,
            CONSTRAINT `PK_Movies` PRIMARY KEY (`Id`),
            INDEX `IX_Movies_BaseItemId` (`BaseItemId`),
            INDEX `IX_Movies_Name` (`Name`)
        );
        """;

    private static string MovieMetadataTableSql() => """
        CREATE TABLE `MovieMetadata` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `MovieId` int NOT NULL,
            `Title` varchar(500) NULL,
            `Language` varchar(10) NULL,
            `IsDefault` tinyint(1) NOT NULL DEFAULT 0,
            CONSTRAINT `PK_MovieMetadata` PRIMARY KEY (`Id`),
            CONSTRAINT `FK_MovieMetadata_Movies_MovieId` FOREIGN KEY (`MovieId`) REFERENCES `Movies` (`Id`) ON DELETE CASCADE
        );
        """;

    private static string MovieUserDataTableSql() => """
        CREATE TABLE `MovieUserData` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `UserId` char(36) NOT NULL,
            `MovieId` int NOT NULL,
            `Played` tinyint(1) NOT NULL DEFAULT 0,
            `PlayCount` int NOT NULL DEFAULT 0,
            `IsFavorite` tinyint(1) NOT NULL DEFAULT 0,
            `LastPlayedDate` datetime(6) NULL,
            CONSTRAINT `PK_MovieUserData` PRIMARY KEY (`Id`),
            INDEX `IX_MovieUserData_UserId` (`UserId`),
            INDEX `IX_MovieUserData_MovieId` (`MovieId`)
        );
        """;

    private static string SeriesTableSql() => """
        CREATE TABLE `Series` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `BaseItemId` char(36) NOT NULL,
            `Name` varchar(500) NULL,
            `Overview` text NULL,
            `ProductionYear` int NULL,
            `Status` varchar(50) NULL,
            `IsActive` tinyint(1) NOT NULL DEFAULT 1,
            `CreatedAt` datetime(6) NOT NULL,
            `UpdatedAt` datetime(6) NOT NULL,
            CONSTRAINT `PK_Series` PRIMARY KEY (`Id`),
            INDEX `IX_Series_BaseItemId` (`BaseItemId`),
            INDEX `IX_Series_Name` (`Name`)
        );
        """;

    private static string SeasonsTableSql() => """
        CREATE TABLE `Seasons` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `SeriesId` int NOT NULL,
            `BaseItemId` char(36) NOT NULL,
            `Name` varchar(500) NULL,
            `IndexNumber` int NULL,
            `IsActive` tinyint(1) NOT NULL DEFAULT 1,
            CONSTRAINT `PK_Seasons` PRIMARY KEY (`Id`),
            INDEX `IX_Seasons_SeriesId` (`SeriesId`),
            CONSTRAINT `FK_Seasons_Series_SeriesId` FOREIGN KEY (`SeriesId`) REFERENCES `Series` (`Id`) ON DELETE CASCADE
        );
        """;

    private static string EpisodesTableSql() => """
        CREATE TABLE `Episodes` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `SeasonId` int NOT NULL,
            `BaseItemId` char(36) NOT NULL,
            `Name` varchar(500) NULL,
            `IndexNumber` int NULL,
            `ParentIndexNumber` int NULL,
            `RunTimeTicks` bigint NULL,
            `IsActive` tinyint(1) NOT NULL DEFAULT 1,
            CONSTRAINT `PK_Episodes` PRIMARY KEY (`Id`),
            INDEX `IX_Episodes_SeasonId` (`SeasonId`),
            INDEX `IX_Episodes_BaseItemId` (`BaseItemId`),
            CONSTRAINT `FK_Episodes_Seasons_SeasonId` FOREIGN KEY (`SeasonId`) REFERENCES `Seasons` (`Id`) ON DELETE CASCADE
        );
        """;

    private static string SeriesUserDataTableSql() => """
        CREATE TABLE `SeriesUserData` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `UserId` char(36) NOT NULL,
            `SeriesId` int NOT NULL,
            `Played` tinyint(1) NOT NULL DEFAULT 0,
            `IsFavorite` tinyint(1) NOT NULL DEFAULT 0,
            `LastPlayedDate` datetime(6) NULL,
            CONSTRAINT `PK_SeriesUserData` PRIMARY KEY (`Id`),
            INDEX `IX_SeriesUserData_UserId` (`UserId`),
            INDEX `IX_SeriesUserData_SeriesId` (`SeriesId`)
        );
        """;

    private static string ChannelsTableSql() => """
        CREATE TABLE `Channels` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `BaseItemId` char(36) NOT NULL,
            `Name` varchar(500) NULL,
            `ChannelNumber` varchar(20) NULL,
            `IsActive` tinyint(1) NOT NULL DEFAULT 1,
            CONSTRAINT `PK_Channels` PRIMARY KEY (`Id`),
            INDEX `IX_Channels_BaseItemId` (`BaseItemId`)
        );
        """;

    private static string ProgramsTableSql() => """
        CREATE TABLE `Programs` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `ChannelId` int NOT NULL,
            `BaseItemId` char(36) NOT NULL,
            `Name` varchar(500) NULL,
            `StartDate` datetime(6) NOT NULL,
            `EndDate` datetime(6) NOT NULL,
            `IsActive` tinyint(1) NOT NULL DEFAULT 1,
            CONSTRAINT `PK_Programs` PRIMARY KEY (`Id`),
            INDEX `IX_Programs_ChannelId` (`ChannelId`),
            INDEX `IX_Programs_StartDate` (`StartDate`),
            CONSTRAINT `FK_Programs_Channels_ChannelId` FOREIGN KEY (`ChannelId`) REFERENCES `Channels` (`Id`) ON DELETE CASCADE
        );
        """;

    private static string BooksTableSql() => """
        CREATE TABLE `Books` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `BaseItemId` char(36) NOT NULL,
            `Name` varchar(500) NULL,
            `Author` varchar(500) NULL,
            `Overview` text NULL,
            `IsActive` tinyint(1) NOT NULL DEFAULT 1,
            CONSTRAINT `PK_Books` PRIMARY KEY (`Id`),
            INDEX `IX_Books_BaseItemId` (`BaseItemId`),
            INDEX `IX_Books_Name` (`Name`)
        );
        """;

    private static string BookUserDataTableSql() => """
        CREATE TABLE `BookUserData` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `UserId` char(36) NOT NULL,
            `BookId` int NOT NULL,
            `Played` tinyint(1) NOT NULL DEFAULT 0,
            `IsFavorite` tinyint(1) NOT NULL DEFAULT 0,
            CONSTRAINT `PK_BookUserData` PRIMARY KEY (`Id`),
            INDEX `IX_BookUserData_UserId` (`UserId`),
            INDEX `IX_BookUserData_BookId` (`BookId`)
        );
        """;
}
