using System.ComponentModel.DataAnnotations;

namespace Jellyfin.Api.Models.UserDtos;

/// <summary>
/// Request DTO to set or update a user's license.
/// </summary>
public class SetUserLicenseRequest
{
    /// <summary>
    /// Gets or sets the duration of the license in hours.
    /// Use -1 or null for unlimited. Common values:
    /// 1 = trial (1 hour), 730 = 1 month, 2190 = 3 months,
    /// 4380 = 6 months, 8760 = 12 months.
    /// </summary>
    public int? DurationHours { get; set; }

    /// <summary>
    /// Gets or sets optional admin notes about this license.
    /// </summary>
    [MaxLength(1024)]
    public string? AdminNotes { get; set; }
}
