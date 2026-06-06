using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Extensions;
using Jellyfin.Api.Models.UserDtos;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// User license management controller.
/// </summary>
[Route("Users/{userId}/License")]
[Authorize(Policy = Policies.RequiresElevation)]
public class UserLicenseController : BaseJellyfinApiController
{
    private readonly IUserLicenseManager _licenseManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<UserLicenseController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserLicenseController"/> class.
    /// </summary>
    /// <param name="licenseManager">Instance of the <see cref="IUserLicenseManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{UserLicenseController}"/> interface.</param>
    public UserLicenseController(
        IUserLicenseManager licenseManager,
        IUserManager userManager,
        ILogger<UserLicenseController> logger)
    {
        _licenseManager = licenseManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets the license for a user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <response code="200">License returned.</response>
    /// <response code="404">User or license not found.</response>
    /// <returns>A <see cref="UserLicenseDto"/> with information about the license.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserLicenseDto>> GetUserLicense(
        [FromRoute, Required] Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return NotFound("User not found.");
        }

        var license = await _licenseManager.GetLicenseAsync(userId).ConfigureAwait(false);
        if (license is null)
        {
            return NotFound("No license found for this user.");
        }

        return Ok(license);
    }

    /// <summary>
    /// Creates or updates a license for a user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="request">The license request.</param>
    /// <response code="200">License created or updated.</response>
    /// <response code="404">User not found.</response>
    /// <returns>A <see cref="UserLicenseDto"/> with the created/updated license.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserLicenseDto>> SetUserLicense(
        [FromRoute, Required] Guid userId,
        [FromBody, Required] SetUserLicenseRequest request)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return NotFound("User not found.");
        }

        var adminUserId = User.GetUserId();

        _logger.LogInformation(
            "Admin {AdminId} setting license for user {UserName} (Id: {UserId}). Duration: {Duration}h.",
            adminUserId,
            user.Username,
            userId,
            request.DurationHours?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unlimited");

        var result = await _licenseManager.SetLicenseAsync(
            userId,
            request.DurationHours,
            request.AdminNotes,
            adminUserId).ConfigureAwait(false);

        return Ok(result);
    }

    /// <summary>
    /// Revokes (deletes) a user's license.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <response code="204">License revoked.</response>
    /// <response code="404">User not found.</response>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RevokeUserLicense(
        [FromRoute, Required] Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return NotFound("User not found.");
        }

        _logger.LogInformation(
            "Admin {AdminId} revoking license for user {UserName} (Id: {UserId}).",
            User.GetUserId(),
            user.Username,
            userId);

        await _licenseManager.RevokeLicenseAsync(userId).ConfigureAwait(false);
        return NoContent();
    }
}
