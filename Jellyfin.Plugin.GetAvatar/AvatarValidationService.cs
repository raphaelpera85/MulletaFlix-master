using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.GetAvatar.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GetAvatar
{
    /// <summary>
    /// Hosted service that validates user avatars at startup.
    /// This ensures that profile images are repaired if they were deleted or lost.
    /// </summary>
    public class AvatarValidationService : IHostedService
    {
        private readonly AvatarService _avatarService;
        private readonly OnlinePackService _onlinePackService;
        private readonly ILogger<AvatarValidationService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="AvatarValidationService"/> class.
        /// </summary>
        /// <param name="avatarService">The avatar service.</param>
        /// <param name="logger">The logger instance.</param>
        public AvatarValidationService(
            AvatarService avatarService,
            OnlinePackService onlinePackService,
            ILogger<AvatarValidationService> logger)
        {
            _avatarService = avatarService;
            _onlinePackService = onlinePackService;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("GetAvatar validation service starting...");

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

                await EnsureDefaultAvatarCatalogAsync().ConfigureAwait(false);

                var assignedCount = await _avatarService.AssignMissingAvatarsAsync().ConfigureAwait(false);
                if (assignedCount > 0)
                {
                    _logger.LogInformation("Auto-assigned avatars to {Count} user(s) without one.", assignedCount);
                }

                var repairedCount = await _avatarService.ValidateUserAvatarsAsync().ConfigureAwait(false);

                if (repairedCount > 0)
                {
                    _logger.LogInformation("Avatar validation completed. Repaired {Count} missing avatar(s).", repairedCount);
                }
                else
                {
                    _logger.LogInformation("Avatar validation completed. All avatars are valid.");
                }

                var deletedCount = _avatarService.CleanOrphanedProfileImages();
                if (deletedCount > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} orphaned profile image(s).", deletedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during avatar validation at startup");
            }
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("GetAvatar validation service stopping...");
            return Task.CompletedTask;
        }

        private async Task EnsureDefaultAvatarCatalogAsync()
        {
            if (_avatarService.GetAvailableAvatars().Count > 0)
            {
                return;
            }

            try
            {
                var packs = await _onlinePackService.GetAvailablePacksAsync(forceRefresh: true).ConfigureAwait(false);
                if (packs.Count == 0)
                {
                    _logger.LogWarning("No online avatar packs were available during startup bootstrap.");
                    return;
                }

                var result = await _onlinePackService.ImportPacksAsync(packs).ConfigureAwait(false);
                _logger.LogInformation(
                    "Bootstrapped {ImportedCount} avatar(s) from {PackCount} online pack(s) at startup.",
                    result.ImportedCount,
                    result.PackResults.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to bootstrap default avatar catalog at startup");
            }
        }
    }
}
