using MulletaFlix.Database.Implementations.Entities.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration;

[DomainConfigurationAttribute]public class MovieMetadataConfiguration : IEntityTypeConfiguration<MovieMetadata>
{
    public void Configure(EntityTypeBuilder<MovieMetadata> builder)
    {
        builder.ToTable("MovieMetadata");
        builder.HasKey(mm => mm.Id);
        builder.Property(mm => mm.Title).HasMaxLength(500);
        builder.Property(mm => mm.Language).HasMaxLength(10);
    }
}
