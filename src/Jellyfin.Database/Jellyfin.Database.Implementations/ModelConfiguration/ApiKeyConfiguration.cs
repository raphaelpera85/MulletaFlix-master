using MulletaFlix.Database.Implementations.Entities.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration
{
    /// <summary>
    /// FluentAPI configuration for the ApiKey entity.
    /// </summary>
    public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
    {
        /// <inheritdoc/>
        public void Configure(EntityTypeBuilder<ApiKey> builder)
        {
            builder
                .HasIndex(entity => entity.AccessToken)
                .IsUnique();
        }
    }
}

