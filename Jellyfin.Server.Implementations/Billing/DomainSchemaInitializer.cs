using System;
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

        var sql = schemaName switch
        {
            "mulletaflix_movies" => MoviesSchema(),
            "mulletaflix_series" => SeriesSchema(),
            "mulletaflix_channels" => ChannelsSchema(),
            "mulletaflix_books" => BooksSchema(),
            "mulletaflix_system" => SystemSchema(),
            _ => ""
        };

        if (!string.IsNullOrEmpty(sql))
        {
            await dbContext.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false);
        }
    }

    private static string MoviesSchema() => """
        CREATE TABLE IF NOT EXISTS `Movies` (
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

        CREATE TABLE IF NOT EXISTS `MovieMetadata` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `MovieId` int NOT NULL,
            `Title` varchar(500) NULL,
            `Language` varchar(10) NULL,
            `IsDefault` tinyint(1) NOT NULL DEFAULT 0,
            CONSTRAINT `PK_MovieMetadata` PRIMARY KEY (`Id`),
            CONSTRAINT `FK_MovieMetadata_Movies_MovieId` FOREIGN KEY (`MovieId`) REFERENCES `Movies` (`Id`) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS `MovieUserData` (
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

    private static string SeriesSchema() => """
        CREATE TABLE IF NOT EXISTS `Series` (
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

        CREATE TABLE IF NOT EXISTS `Seasons` (
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

        CREATE TABLE IF NOT EXISTS `Episodes` (
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

        CREATE TABLE IF NOT EXISTS `SeriesUserData` (
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

    private static string ChannelsSchema() => """
        CREATE TABLE IF NOT EXISTS `Channels` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `BaseItemId` char(36) NOT NULL,
            `Name` varchar(500) NULL,
            `ChannelNumber` varchar(20) NULL,
            `IsActive` tinyint(1) NOT NULL DEFAULT 1,
            CONSTRAINT `PK_Channels` PRIMARY KEY (`Id`),
            INDEX `IX_Channels_BaseItemId` (`BaseItemId`)
        );

        CREATE TABLE IF NOT EXISTS `Programs` (
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

    private static string BooksSchema() => """
        CREATE TABLE IF NOT EXISTS `Books` (
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

        CREATE TABLE IF NOT EXISTS `BookUserData` (
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

    private static string SystemSchema() => """
        CREATE TABLE IF NOT EXISTS `DeviceOptions` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `DeviceId` varchar(256) NOT NULL,
            `Key` varchar(256) NOT NULL,
            `Value` text NULL,
            CONSTRAINT `PK_DeviceOptions` PRIMARY KEY (`Id`),
            INDEX `IX_DeviceOptions_DeviceId` (`DeviceId`)
        );

        CREATE TABLE IF NOT EXISTS `ApiKeys` (
            `Id` int NOT NULL AUTO_INCREMENT,
            `Name` varchar(64) NOT NULL,
            `AccessToken` varchar(256) NOT NULL,
            `IsActive` tinyint(1) NOT NULL DEFAULT 1,
            `CreatedAt` datetime(6) NOT NULL,
            CONSTRAINT `PK_ApiKeys` PRIMARY KEY (`Id`),
            INDEX `IX_ApiKeys_AccessToken` (`AccessToken`)
        );
        """;
}
