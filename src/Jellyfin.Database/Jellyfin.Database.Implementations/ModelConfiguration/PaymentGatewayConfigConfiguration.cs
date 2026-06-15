using MulletaFlix.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration
{
    public class PaymentGatewayConfigConfiguration : IEntityTypeConfiguration<PaymentGatewayConfig>
    {
        public void Configure(EntityTypeBuilder<PaymentGatewayConfig> builder)
        {
            builder.HasIndex(g => g.GatewayName).IsUnique();
            builder.Property(g => g.GatewayName).HasMaxLength(50).IsRequired();
            builder.Property(g => g.DisplayName).HasMaxLength(100).IsRequired();
            builder.Property(g => g.PublicKey).HasMaxLength(200);
        }
    }
}
