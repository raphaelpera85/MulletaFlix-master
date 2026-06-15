using MulletaFlix.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration
{
    public class DiscountCouponConfiguration : IEntityTypeConfiguration<DiscountCoupon>
    {
        public void Configure(EntityTypeBuilder<DiscountCoupon> builder)
        {
            builder.HasIndex(c => c.Code).IsUnique();
            builder.Property(c => c.Code).HasMaxLength(50).IsRequired();
            builder.Property(c => c.DiscountPercent).HasPrecision(5, 2);
            builder.Property(c => c.DiscountFixed).HasPrecision(18, 2);
            builder.Property(c => c.MinOrderAmount).HasPrecision(18, 2);
        }
    }
}
