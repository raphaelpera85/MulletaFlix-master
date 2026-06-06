using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Dto;

namespace MediaBrowser.Controller.Library;

/// <summary>
/// Interface for managing user licenses/subscriptions.
/// </summary>
public interface IUserLicenseManager
{
    /// <summary>
    /// Gets the license for a user.
    /// </summary>
    /// <param name="userId">The user's Id.</param>
    /// <returns>The license DTO, or null if no license exists.</returns>
    Task<UserLicenseDto?> GetLicenseAsync(Guid userId);

    /// <summary>
    /// Creates or updates a license for a user.
    /// If the user already has an active license, the remaining time is accumulated.
    /// </summary>
    /// <param name="userId">The user's Id.</param>
    /// <param name="durationHours">Duration in hours, or null/-1 for unlimited.</param>
    /// <param name="adminNotes">Optional admin notes.</param>
    /// <param name="grantedByUserId">The admin who granted the license.</param>
    /// <returns>The created or updated license DTO.</returns>
    Task<UserLicenseDto> SetLicenseAsync(Guid userId, int? durationHours, string? adminNotes, Guid grantedByUserId);

    /// <summary>
    /// Revokes (deletes) a user's license.
    /// </summary>
    /// <param name="userId">The user's Id.</param>
    /// <returns>A task representing the revocation.</returns>
    Task RevokeLicenseAsync(Guid userId);

    /// <summary>
    /// Checks all licenses and disables users whose licenses have expired.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of users disabled.</returns>
    Task<int> ExpireOutdatedLicensesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether a specific user's license is expired.
    /// </summary>
    /// <param name="userId">The user's Id.</param>
    /// <returns>True if the user has a license and it is expired; false otherwise.</returns>
    Task<bool> IsLicenseExpiredAsync(Guid userId);
}
