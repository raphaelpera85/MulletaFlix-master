using MulletaFlix.Database.Implementations.Entities.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration;

[DomainConfigurationAttribute]public class BookUserDataConfiguration : IEntityTypeConfiguration<BookUserData>
{
    public void Configure(EntityTypeBuilder<BookUserData> builder)
    {
        builder.ToTable("BookUserData");
        builder.HasKey(ud => ud.Id);
        builder.HasIndex(ud => ud.UserId);
        builder.HasIndex(ud => ud.BookId);
    }
}
