namespace Jellyfin.Plugin.GetAvatar.Controllers
{
    /// <summary>
    /// Request model for setting user avatar.
    /// </summary>
    public class SetAvatarRequest
    {
        /// <summary>
        /// Gets or sets the avatar ID.
        /// </summary>
        public string AvatarId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target user ID (optional, defaults to current user if not provided).
        /// </summary>
        public string? UserId { get; set; }
    }
}
