using MulletaFlix.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration
{
    public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
    {
        public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
        {
            builder.HasKey(t => t.Id);
            builder.HasIndex(t => t.GatewayTransactionId).IsUnique();
            builder.HasIndex(t => t.UserId);
            builder.HasIndex(t => t.PricingPlanId);
            builder.HasIndex(t => t.Status);

            builder.HasOne(t => t.User)
                .WithMany(u => u.PaymentTransactions)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(t => t.PricingPlan)
                .WithMany(p => p.PaymentTransactions)
                .HasForeignKey(t => t.PricingPlanId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(t => t.Coupon)
                .WithMany(c => c.PaymentTransactions)
                .HasForeignKey(t => t.CouponId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Property(t => t.GatewayName).HasMaxLength(50).IsRequired();
            builder.Property(t => t.GatewayTransactionId).HasMaxLength(200).IsRequired();
            builder.Property(t => t.Amount).HasPrecision(18, 2);
            builder.Property(t => t.DiscountAmount).HasPrecision(18, 2);
            builder.Property(t => t.CustomerEmail).HasMaxLength(256).IsRequired();
            builder.Property(t => t.CustomerName).HasMaxLength(256).IsRequired();
            builder.Property(t => t.CustomerPhone).HasMaxLength(20);
            builder.Property(t => t.IpAddress).HasMaxLength(45).IsRequired();
            builder.Property(t => t.UserAgent).HasMaxLength(500);
        }
    }
}
