using MulletaFlix.Database.Implementations.Entities.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration;

[DomainConfigurationAttribute]public class MovieUserDataConfiguration : IEntityTypeConfiguration<MovieUserData>
{
    public void Configure(EntityTypeBuilder<MovieUserData> builder)
    {
        builder.ToTable("MovieUserData");
        builder.HasKey(ud => ud.Id);
        builder.HasIndex(ud => ud.UserId);
        builder.HasIndex(ud => ud.MovieId);
    }
}
