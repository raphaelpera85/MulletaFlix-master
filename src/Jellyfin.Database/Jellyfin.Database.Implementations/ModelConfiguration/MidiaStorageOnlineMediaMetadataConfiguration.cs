using MulletaFlix.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration;

public class MidiaStorageOnlineMediaMetadataConfiguration : IEntityTypeConfiguration<MidiaStorageOnlineMediaMetadata>
{
    public void Configure(EntityTypeBuilder<MidiaStorageOnlineMediaMetadata> builder)
    {
        builder.ToTable("MidiaStorageOnlineMediaMetadata");
        builder.HasKey(x => x.RelativePath);
        builder.Property(x => x.RelativePath).HasMaxLength(512);
        builder.Property(x => x.ContentType).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Mode).HasMaxLength(16).IsRequired();
        builder.Property(x => x.SourceUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.SourceId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.RecognizedAtUtc).IsRequired();
        builder.HasIndex(x => x.SourceId);
    }
}
