using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using MulletaFlix.Api.Models.StartupDtos;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MulletaFlix.Api.Controllers;

/// <summary>
/// The startup wizard controller.
/// </summary>
[Authorize(Policy = Policies.FirstTimeSetupOrElevated)]
public class StartupController : BaseMulletaFlixApiController
{
    private const string DefaultServerName = "Mulletaflix";
    private readonly IServerConfigurationManager _config;
    private readonly IUserManager _userManager;
    private readonly ILocalizationManager _localizationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupController" /> class.
    /// </summary>
    /// <param name="config">The server configuration manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="localizationManager">The localization manager.</param>
    public StartupController(IServerConfigurationManager config, IUserManager userManager, ILocalizationManager localizationManager)
    {
        _config = config;
        _userManager = userManager;
        _localizationManager = localizationManager;
    }

    /// <summary>
    /// Completes the startup wizard.
    /// </summary>
    /// <response code="204">Startup wizard completed.</response>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    [HttpPost("Complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult CompleteWizard()
    {
        _config.Configuration.IsStartupWizardCompleted = true;
        _config.SaveConfiguration();
        return NoContent();
    }

    /// <summary>
    /// Gets the initial startup wizard configuration.
    /// </summary>
    /// <response code="200">Initial startup wizard configuration retrieved.</response>
    /// <returns>An <see cref="OkResult"/> containing the initial startup wizard configuration.</returns>
    [HttpGet("Configuration")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Obsolete("Use configuration endpoints")]
    public ActionResult<StartupConfigurationDto> GetStartupConfiguration()
    {
        var metadataCountryCode = _config.Configuration.MetadataCountryCode ?? string.Empty;
        var preferredMetadataLanguage = _config.Configuration.PreferredMetadataLanguage;
        if (string.IsNullOrWhiteSpace(preferredMetadataLanguage))
        {
            preferredMetadataLanguage = _localizationManager.GetDefaultMetadataLanguage(metadataCountryCode);
        }

        return new StartupConfigurationDto
        {
            ServerName = string.IsNullOrWhiteSpace(_config.Configuration.ServerName) ? DefaultServerName : _config.Configuration.ServerName,
            UICulture = _config.Configuration.UICulture,
            MetadataCountryCode = metadataCountryCode,
            PreferredMetadataLanguage = preferredMetadataLanguage
        };
    }

    /// <summary>
    /// Sets the initial startup wizard configuration.
    /// </summary>
    /// <param name="startupConfiguration">The updated startup configuration.</param>
    /// <response code="204">Configuration saved.</response>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    [HttpPost("Configuration")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Obsolete("Use configuration endpoints")]
    public ActionResult UpdateInitialConfiguration([FromBody, Required] StartupConfigurationDto startupConfiguration)
    {
        _config.Configuration.ServerName = string.IsNullOrWhiteSpace(startupConfiguration.ServerName) ? DefaultServerName : startupConfiguration.ServerName;
        _config.Configuration.UICulture = startupConfiguration.UICulture ?? string.Empty;
        var metadataCountryCode = startupConfiguration.MetadataCountryCode ?? string.Empty;
        _config.Configuration.MetadataCountryCode = metadataCountryCode;
        _config.Configuration.PreferredMetadataLanguage = string.IsNullOrWhiteSpace(startupConfiguration.PreferredMetadataLanguage)
            ? _localizationManager.GetDefaultMetadataLanguage(metadataCountryCode)
            : startupConfiguration.PreferredMetadataLanguage;
        _config.SaveConfiguration();
        return NoContent();
    }

    /// <summary>
    /// Sets remote access and UPnP.
    /// </summary>
    /// <param name="startupRemoteAccessDto">The startup remote access dto.</param>
    /// <response code="204">Configuration saved.</response>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    [HttpPost("RemoteAccess")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Obsolete("Use configuration endpoints")]
    public ActionResult SetRemoteAccess([FromBody, Required] StartupRemoteAccessDto startupRemoteAccessDto)
    {
        NetworkConfiguration settings = _config.GetNetworkConfiguration();
        settings.EnableRemoteAccess = startupRemoteAccessDto.EnableRemoteAccess;
        _config.SaveConfiguration(NetworkConfigurationStore.StoreKey, settings);
        return NoContent();
    }

    /// <summary>
    /// Gets the first user.
    /// </summary>
    /// <response code="200">Initial user retrieved.</response>
    /// <returns>The first user.</returns>
    [HttpGet("User")]
    [HttpGet("FirstUser", Name = "GetFirstUser_2")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Obsolete("Use authentication endpoints")]
    public async Task<StartupUserDto> GetFirstUser()
    {
        // TODO: Remove this method when startup wizard no longer requires an existing user.
        await _userManager.InitializeAsync().ConfigureAwait(false);
        var user = _userManager.GetFirstUser() ?? throw new InvalidOperationException("No user exists after initialization.");
        return new StartupUserDto
        {
            Name = user.Username
        };
    }

    /// <summary>
    /// Sets the user name and password.
    /// </summary>
    /// <param name="startupUserDto">The DTO containing username and password.</param>
    /// <response code="204">Updated user name and password.</response>
    /// <returns>
    /// A <see cref="Task" /> that represents the asynchronous update operation.
    /// The task result contains a <see cref="NoContentResult"/> indicating success.
    /// </returns>
    [HttpPost("User")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> UpdateStartupUser([FromBody] StartupUserDto startupUserDto)
    {
        var user = _userManager.GetFirstUser();
        if (user is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(startupUserDto.Password))
        {
            return BadRequest("Password must not be empty");
        }

        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

#pragma warning disable CA1309 // Use ordinal string comparison
        if (startupUserDto.Name is not null && !startupUserDto.Name.Equals(user.Username, StringComparison.InvariantCultureIgnoreCase))
        {
            await _userManager.RenameUser(user.Id, user.Username, startupUserDto.Name).ConfigureAwait(false);
        }
#pragma warning restore CA1309 // Use ordinal string comparison

        if (!string.IsNullOrEmpty(startupUserDto.Password))
        {
            await _userManager.ChangePassword(user.Id, startupUserDto.Password).ConfigureAwait(false);
        }

        return NoContent();
    }
}

