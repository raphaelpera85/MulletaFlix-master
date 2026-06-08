using MulletaFlix.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the UserLicense entity.
/// </summary>
public class UserLicenseConfiguration : IEntityTypeConfiguration<UserLicense>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<UserLicense> builder)
    {
        builder
            .HasOne(l => l.User)
            .WithOne(u => u.License)
            .HasForeignKey<UserLicense>(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(l => l.UserId)
            .IsUnique();

        builder
            .HasIndex(l => l.ExpirationDate);
    }
}

