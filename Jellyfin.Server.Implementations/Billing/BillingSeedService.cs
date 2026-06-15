using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Database.Implementations.Contexts;
using MulletaFlix.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace MulletaFlix.Server.Implementations.Billing;

public static class BillingSeedService
{
    private static readonly IReadOnlyList<PricingPlan> DefaultPlans =
    [
        new PricingPlan
        {
            Name = "1 mês",
            DurationMonths = 1,
            PricePerMonth = 20m,
            TotalPrice = 20m,
            IsActive = true,
            IsHighlighted = false,
            SortOrder = 1
        },
        new PricingPlan
        {
            Name = "3 meses",
            DurationMonths = 3,
            PricePerMonth = 18m,
            TotalPrice = 54m,
            IsActive = true,
            IsHighlighted = false,
            SortOrder = 2
        },
        new PricingPlan
        {
            Name = "6 meses",
            DurationMonths = 6,
            PricePerMonth = 17m,
            TotalPrice = 102m,
            IsActive = true,
            IsHighlighted = false,
            SortOrder = 3
        },
        new PricingPlan
        {
            Name = "12 meses",
            DurationMonths = 12,
            PricePerMonth = 15m,
            TotalPrice = 180m,
            IsActive = true,
            IsHighlighted = true,
            SortOrder = 4
        }
    ];

    private static readonly IReadOnlyList<PaymentGatewayConfig> DefaultGateways =
    [
        new PaymentGatewayConfig
        {
            GatewayName = "MercadoPago",
            DisplayName = "Mercado Pago",
            IsEnabled = false,
            IsPrimary = false,
            AccessToken = string.Empty,
            PublicKey = string.Empty,
            WebhookSecret = string.Empty,
            SandboxMode = true,
            EnablePix = true,
            EnableCredit = true,
            EnableDebit = true
        },
        new PaymentGatewayConfig
        {
            GatewayName = "PagSeguro",
            DisplayName = "PagSeguro",
            IsEnabled = false,
            IsPrimary = false,
            AccessToken = string.Empty,
            PublicKey = string.Empty,
            WebhookSecret = string.Empty,
            SandboxMode = true,
            EnablePix = true,
            EnableCredit = true,
            EnableDebit = true
        }
    ];

    public static async Task SeedAsync(UsersDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(dbContext, cancellationToken).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        var changed = false;

        foreach (var plan in DefaultPlans)
        {
            var existingPlan = await dbContext.PricingPlans
                .FirstOrDefaultAsync(p => p.DurationMonths == plan.DurationMonths, cancellationToken)
                .ConfigureAwait(false);

            if (existingPlan is not null)
            {
                continue;
            }

            dbContext.PricingPlans.Add(new PricingPlan
            {
                Name = plan.Name,
                DurationMonths = plan.DurationMonths,
                PricePerMonth = plan.PricePerMonth,
                TotalPrice = plan.TotalPrice,
                IsActive = plan.IsActive,
                IsHighlighted = plan.IsHighlighted,
                SortOrder = plan.SortOrder,
                CreatedAt = now,
                UpdatedAt = now
            });
            changed = true;
        }

        foreach (var gateway in DefaultGateways)
        {
            var existingGateway = await dbContext.PaymentGatewayConfigs
                .FirstOrDefaultAsync(g => g.GatewayName == gateway.GatewayName, cancellationToken)
                .ConfigureAwait(false);

            if (existingGateway is not null)
            {
                continue;
            }

            dbContext.PaymentGatewayConfigs.Add(new PaymentGatewayConfig
            {
                GatewayName = gateway.GatewayName,
                DisplayName = gateway.DisplayName,
                IsEnabled = gateway.IsEnabled,
                IsPrimary = gateway.IsPrimary,
                AccessToken = gateway.AccessToken,
                PublicKey = gateway.PublicKey,
                WebhookSecret = gateway.WebhookSecret,
                SandboxMode = gateway.SandboxMode,
                EnablePix = gateway.EnablePix,
                EnableCredit = gateway.EnableCredit,
                EnableDebit = gateway.EnableDebit,
                CreatedAt = now,
                UpdatedAt = now
            });
            changed = true;
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureSchemaAsync(UsersDbContext dbContext, CancellationToken cancellationToken)
    {
        var databaseCreator = dbContext.Database.GetService<IDatabaseCreator>() as IRelationalDatabaseCreator
            ?? throw new InvalidOperationException("Billing seed requires a relational database provider.");

        if (!await databaseCreator.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            await databaseCreator.CreateAsync(cancellationToken).ConfigureAwait(false);
        }

        await EnsureBillingTablesAsync(dbContext, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureBillingTablesAsync(UsersDbContext dbContext, CancellationToken cancellationToken)
    {
        var providerName = dbContext.Database.ProviderName ?? string.Empty;
        var supportsSqlite = providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var supportsMySql = providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
                            providerName.Contains("MariaDb", StringComparison.OrdinalIgnoreCase);

        if (supportsSqlite)
        {
            const string createSqliteBillingSchemaSql = """
                CREATE TABLE IF NOT EXISTS "PricingPlans" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_PricingPlans" PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL,
                    "DurationMonths" INTEGER NOT NULL,
                    "PricePerMonth" TEXT NOT NULL,
                    "TotalPrice" TEXT NOT NULL,
                    "IsActive" INTEGER NOT NULL,
                    "IsHighlighted" INTEGER NOT NULL,
                    "SortOrder" INTEGER NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL,
                    CONSTRAINT "AK_PricingPlans_DurationMonths" UNIQUE ("DurationMonths")
                );

                CREATE TABLE IF NOT EXISTS "PaymentGatewayConfigs" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_PaymentGatewayConfigs" PRIMARY KEY AUTOINCREMENT,
                    "GatewayName" TEXT NOT NULL,
                    "DisplayName" TEXT NOT NULL,
                    "IsEnabled" INTEGER NOT NULL,
                    "IsPrimary" INTEGER NOT NULL,
                    "AccessToken" TEXT NOT NULL,
                    "PublicKey" TEXT NOT NULL,
                    "WebhookSecret" TEXT NOT NULL,
                    "SandboxMode" INTEGER NOT NULL,
                    "EnablePix" INTEGER NOT NULL,
                    "EnableCredit" INTEGER NOT NULL,
                    "EnableDebit" INTEGER NOT NULL,
                    "ExtraConfig" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL,
                    CONSTRAINT "AK_PaymentGatewayConfigs_GatewayName" UNIQUE ("GatewayName")
                );
                """;

            await dbContext.Database.ExecuteSqlRawAsync(
                createSqliteBillingSchemaSql,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (supportsMySql)
        {
            // Tables are created by EF Core migration (InitialMySqlCreate).
            // Raw SQL fallback ensures they exist even if migration hasn't run yet.
            const string createMySqlBillingSchemaSql = """
                CREATE TABLE IF NOT EXISTS `PricingPlans` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `Name` varchar(100) NOT NULL,
                    `DurationMonths` int NOT NULL,
                    `PricePerMonth` decimal(18,2) NOT NULL,
                    `TotalPrice` decimal(18,2) NOT NULL,
                    `IsActive` tinyint(1) NOT NULL,
                    `IsHighlighted` tinyint(1) NOT NULL,
                    `SortOrder` int NOT NULL,
                    `CreatedAt` datetime(6) NOT NULL,
                    `UpdatedAt` datetime(6) NOT NULL,
                    CONSTRAINT `PK_PricingPlans` PRIMARY KEY (`Id`),
                    CONSTRAINT `AK_PricingPlans_DurationMonths` UNIQUE (`DurationMonths`)
                );

                CREATE TABLE IF NOT EXISTS `PaymentGatewayConfigs` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `GatewayName` varchar(50) NOT NULL,
                    `DisplayName` varchar(100) NOT NULL,
                    `IsEnabled` tinyint(1) NOT NULL,
                    `IsPrimary` tinyint(1) NOT NULL,
                    `AccessToken` longtext NOT NULL,
                    `PublicKey` varchar(200) NULL,
                    `WebhookSecret` longtext NOT NULL,
                    `SandboxMode` tinyint(1) NOT NULL,
                    `EnablePix` tinyint(1) NOT NULL,
                    `EnableCredit` tinyint(1) NOT NULL,
                    `EnableDebit` tinyint(1) NOT NULL,
                    `ExtraConfig` longtext NULL,
                    `CreatedAt` datetime(6) NOT NULL,
                    `UpdatedAt` datetime(6) NOT NULL,
                    CONSTRAINT `PK_PaymentGatewayConfigs` PRIMARY KEY (`Id`),
                    CONSTRAINT `AK_PaymentGatewayConfigs_GatewayName` UNIQUE (`GatewayName`)
                );
                """;

            await dbContext.Database.ExecuteSqlRawAsync(
                createMySqlBillingSchemaSql,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException($"Billing seed does not support provider '{providerName}'.");
    }
}
