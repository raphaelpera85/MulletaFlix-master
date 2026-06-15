using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Database.Implementations.Contexts;
using MulletaFlix.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;

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
}
