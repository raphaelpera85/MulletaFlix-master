using MulletaFlix.Database.Implementations.Entities.Series;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration;

[DomainConfigurationAttribute]public class SeriesConfiguration : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        builder.ToTable("Series");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(500);
        builder.Property(s => s.Overview).HasColumnType("text");
        builder.Property(s => s.Status).HasMaxLength(50);
        builder.HasIndex(s => s.BaseItemId);
        builder.HasIndex(s => s.Name);

        builder.HasMany(s => s.Seasons)
            .WithOne(se => se.Series)
            .HasForeignKey(se => se.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.UserData)
            .WithOne(ud => ud.Series)
            .HasForeignKey(ud => ud.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
