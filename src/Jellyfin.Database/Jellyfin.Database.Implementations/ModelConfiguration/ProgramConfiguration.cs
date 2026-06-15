using MulletaFlix.Database.Implementations.Entities.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration;

[DomainConfigurationAttribute]public class ProgramConfiguration : IEntityTypeConfiguration<Program>
{
    public void Configure(EntityTypeBuilder<Program> builder)
    {
        builder.ToTable("Programs");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(500);
        builder.HasIndex(p => p.ChannelId);
        builder.HasIndex(p => p.StartDate);
    }
}
