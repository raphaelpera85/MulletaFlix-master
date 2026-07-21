using System.Threading.Tasks;
using MulletaFlix.Plugin.GetAvatar.Services;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;
using MulletaFlix.Data.Events.Users;

namespace MulletaFlix.Plugin.GetAvatar
{
    /// <summary>
    /// Applies a random avatar when a new user is created.
    /// </summary>
    public class UserCreatedAvatarConsumer : IEventConsumer<UserCreatedEventArgs>
    {
        private readonly AvatarService _avatarService;
        private readonly ILogger<UserCreatedAvatarConsumer> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserCreatedAvatarConsumer"/> class.
        /// </summary>
        /// <param name="avatarService">The avatar service.</param>
        /// <param name="logger">The logger instance.</param>
        public UserCreatedAvatarConsumer(AvatarService avatarService, ILogger<UserCreatedAvatarConsumer> logger)
        {
            _avatarService = avatarService;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task OnEvent(UserCreatedEventArgs eventArgs)
        {
            if (eventArgs.Argument is null)
            {
                return;
            }

            if (await _avatarService.AssignRandomAvatarAsync(eventArgs.Argument, forceAssign: true).ConfigureAwait(false))
            {
                _logger.LogInformation(
                    "Assigned a random avatar to newly created user {UserName}",
                    eventArgs.Argument.Username);
            }
        }
    }
}
