using MulletaFlix.Database.Implementations.Entities.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration;

[DomainConfigurationAttribute]public class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.ToTable("Movies");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).HasMaxLength(500);
        builder.Property(m => m.Overview).HasColumnType("text");
        builder.Property(m => m.Runtime).HasColumnType("double");
        builder.Property(m => m.CommunityRating).HasColumnType("float");
        builder.HasIndex(m => m.BaseItemId);
        builder.HasIndex(m => m.Name);

        builder.HasMany(m => m.Metadata)
            .WithOne(mm => mm.Movie)
            .HasForeignKey(mm => mm.MovieId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.UserData)
            .WithOne(ud => ud.Movie)
            .HasForeignKey(ud => ud.MovieId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
