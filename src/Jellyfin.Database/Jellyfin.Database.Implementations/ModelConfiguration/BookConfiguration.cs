using MulletaFlix.Database.Implementations.Entities.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration;

[DomainConfigurationAttribute]public class BookConfiguration : IEntityTypeConfiguration<Book>
{
    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("Books");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Name).HasMaxLength(500);
        builder.Property(b => b.Author).HasMaxLength(500);
        builder.Property(b => b.Overview).HasColumnType("text");
        builder.HasIndex(b => b.BaseItemId);
        builder.HasIndex(b => b.Name);

        builder.HasMany(b => b.UserData)
            .WithOne(ud => ud.Book)
            .HasForeignKey(ud => ud.BookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
