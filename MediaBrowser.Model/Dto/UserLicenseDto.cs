#nullable disable

using System;

namespace MediaBrowser.Model.Dto;

/// <summary>
/// DTO representing a user's license/subscription.
/// </summary>
public class UserLicenseDto
{
    /// <summary>
    /// Gets or sets the user's Id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the user's name.
    /// </summary>
    public string UserName { get; set; }

    /// <summary>
    /// Gets or sets the date when the license was activated.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Gets or sets the duration of the license in hours. Null for unlimited.
    /// </summary>
    public int? DurationHours { get; set; }

    /// <summary>
    /// Gets or sets the expiration date. Null for unlimited.
    /// </summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the license is unlimited.
    /// </summary>
    public bool IsUnlimited { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the license has expired.
    /// </summary>
    public bool IsExpired { get; set; }

    /// <summary>
    /// Gets or sets a human-readable string for the time remaining.
    /// </summary>
    public string TimeRemaining { get; set; }

    /// <summary>
    /// Gets or sets optional admin notes.
    /// </summary>
    public string AdminNotes { get; set; }

    /// <summary>
    /// Gets or sets the name of the admin who granted this license.
    /// </summary>
    public string GrantedByUserName { get; set; }

    /// <summary>
    /// Gets or sets the date when the license was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date when the license was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
