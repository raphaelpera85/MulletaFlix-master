using MulletaFlix.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration
{
    public class PricingPlanConfiguration : IEntityTypeConfiguration<PricingPlan>
    {
        public void Configure(EntityTypeBuilder<PricingPlan> builder)
        {
            builder.HasIndex(p => p.DurationMonths).IsUnique();
            builder.HasIndex(p => p.SortOrder);
            builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
            builder.Property(p => p.PricePerMonth).HasPrecision(18, 2);
            builder.Property(p => p.TotalPrice).HasPrecision(18, 2);

            builder.HasMany(p => p.PaymentTransactions)
                .WithOne(t => t.PricingPlan)
                .HasForeignKey(t => t.PricingPlanId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
