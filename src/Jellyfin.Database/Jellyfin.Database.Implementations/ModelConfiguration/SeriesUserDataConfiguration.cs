using MulletaFlix.Database.Implementations.Entities.Series;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration;

[DomainConfigurationAttribute]public class SeriesUserDataConfiguration : IEntityTypeConfiguration<SeriesUserData>
{
    public void Configure(EntityTypeBuilder<SeriesUserData> builder)
    {
        builder.ToTable("SeriesUserData");
        builder.HasKey(ud => ud.Id);
        builder.HasIndex(ud => ud.UserId);
        builder.HasIndex(ud => ud.SeriesId);
    }
}
