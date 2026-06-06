namespace Jellyfin.Api.Models.UserDtos;

/// <summary>
/// The register user response body.
/// </summary>
public class RegisterUserResult
{
    /// <summary>
    /// Gets or sets a value indicating whether registration succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets a translation key or error message for the result.
    /// </summary>
    public string? Message { get; set; }
}
