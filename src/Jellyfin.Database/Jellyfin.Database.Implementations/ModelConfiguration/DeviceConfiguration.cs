using MulletaFlix.Database.Implementations.Entities.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MulletaFlix.Database.Implementations.ModelConfiguration
{
    /// <summary>
    /// FluentAPI configuration for the Device entity.
    /// </summary>
    public class DeviceConfiguration : IEntityTypeConfiguration<Device>
    {
        /// <inheritdoc/>
        public void Configure(EntityTypeBuilder<Device> builder)
        {
            builder
                .HasIndex(entity => new { entity.DeviceId, entity.DateLastActivity });

            builder
                .HasIndex(entity => new { entity.AccessToken, entity.DateLastActivity });

            builder
                .HasIndex(entity => new { entity.UserId, entity.DeviceId });
        }
    }
}

