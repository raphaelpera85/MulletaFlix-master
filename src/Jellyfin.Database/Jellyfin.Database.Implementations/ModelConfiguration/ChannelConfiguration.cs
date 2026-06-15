using MulletaFlix.Database.Implementations.Entities.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration;

[DomainConfigurationAttribute]public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.ToTable("Channels");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(500);
        builder.Property(c => c.ChannelNumber).HasMaxLength(20);
        builder.HasIndex(c => c.BaseItemId);

        builder.HasMany(c => c.Programs)
            .WithOne(p => p.Channel)
            .HasForeignKey(p => p.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
