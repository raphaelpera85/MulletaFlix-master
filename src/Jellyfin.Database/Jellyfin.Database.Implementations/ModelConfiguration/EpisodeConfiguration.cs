using MulletaFlix.Database.Implementations.Entities.Series;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration;

[DomainConfigurationAttribute]public class EpisodeConfiguration : IEntityTypeConfiguration<Episode>
{
    public void Configure(EntityTypeBuilder<Episode> builder)
    {
        builder.ToTable("Episodes");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(500);
        builder.HasIndex(e => e.SeasonId);
        builder.HasIndex(e => e.BaseItemId);
    }
}
