using System;
using System.Linq;
using System.Threading.Tasks;
using MulletaFlix.Database.Implementations.Contexts;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Server.Implementations.Billing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Users;

public sealed class BillingDatabaseTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly UsersDbContext _context;

    public BillingDatabaseTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<UsersDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new UsersDbContext(options, NullLogger<UsersDbContext>.Instance);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SeedBillingDefaultsAsync_CreatesFourDefaultPlansAndBaseGateways()
    {
        await BillingSeedService.SeedAsync(_context);

        var plans = await _context.PricingPlans
            .AsNoTracking()
            .OrderBy(p => p.DurationMonths)
            .ToListAsync();

        Assert.Equal(4, plans.Count);
        Assert.Collection(plans,
            plan =>
            {
                Assert.Equal(1, plan.DurationMonths);
                Assert.Equal("1 mês", plan.Name);
                Assert.True(plan.IsActive);
            },
            plan =>
            {
                Assert.Equal(3, plan.DurationMonths);
                Assert.Equal("3 meses", plan.Name);
                Assert.True(plan.IsActive);
            },
            plan =>
            {
                Assert.Equal(6, plan.DurationMonths);
                Assert.Equal("6 meses", plan.Name);
                Assert.True(plan.IsActive);
            },
            plan =>
            {
                Assert.Equal(12, plan.DurationMonths);
                Assert.Equal("12 meses", plan.Name);
                Assert.True(plan.IsActive);
            });

        var gateways = await _context.PaymentGatewayConfigs
            .AsNoTracking()
            .OrderBy(g => g.GatewayName)
            .ToListAsync();

        Assert.Equal(2, gateways.Count);
        Assert.Contains(gateways, g => g.GatewayName == "MercadoPago" && !g.IsEnabled);
        Assert.Contains(gateways, g => g.GatewayName == "PagSeguro" && !g.IsEnabled);
    }

    [Fact]
    public async Task SeedBillingDefaultsAsync_IsIdempotentAndPreservesAdminChanges()
    {
        await BillingSeedService.SeedAsync(_context);

        var monthlyPlan = await _context.PricingPlans.SingleAsync(p => p.DurationMonths == 1);
        monthlyPlan.Name = "Plano de teste alterado";
        monthlyPlan.PricePerMonth = 99.99m;
        await _context.SaveChangesAsync();

        await BillingSeedService.SeedAsync(_context);

        var plans = await _context.PricingPlans.AsNoTracking().ToListAsync();
        Assert.Equal(4, plans.Count);

        var updatedMonthlyPlan = await _context.PricingPlans.AsNoTracking().SingleAsync(p => p.DurationMonths == 1);
        Assert.Equal("Plano de teste alterado", updatedMonthlyPlan.Name);
        Assert.Equal(99.99m, updatedMonthlyPlan.PricePerMonth);
    }

    [Fact]
    public void BillingModel_ContainsRequiredUniqueIndexesAndRelations()
    {
        var pricingPlanEntity = _context.Model.FindEntityType(typeof(PricingPlan));
        Assert.NotNull(pricingPlanEntity);
        Assert.Contains(pricingPlanEntity!.GetIndexes(), index => index.Properties.Select(p => p.Name).SequenceEqual([nameof(PricingPlan.DurationMonths)]));

        var gatewayEntity = _context.Model.FindEntityType(typeof(PaymentGatewayConfig));
        Assert.NotNull(gatewayEntity);
        Assert.Contains(gatewayEntity!.GetIndexes(), index => index.Properties.Select(p => p.Name).SequenceEqual([nameof(PaymentGatewayConfig.GatewayName)]));

        var transactionEntity = _context.Model.FindEntityType(typeof(PaymentTransaction));
        Assert.NotNull(transactionEntity);
        Assert.Contains(transactionEntity!.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(PricingPlan) &&
            fk.Properties.Select(p => p.Name).SequenceEqual([nameof(PaymentTransaction.PricingPlanId)]));

        Assert.Contains(transactionEntity!.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(DiscountCoupon) &&
            fk.Properties.Select(p => p.Name).SequenceEqual([nameof(PaymentTransaction.CouponId)]));
    }
}
