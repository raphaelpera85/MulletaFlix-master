using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
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
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<StartupController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupController" /> class.
    /// </summary>
    /// <param name="config">The server configuration manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="localizationManager">The localization manager.</param>
    /// <param name="logger">The logger.</param>
    public StartupController(
        IServerConfigurationManager config,
        IUserManager userManager,
        ILocalizationManager localizationManager,
        ILogger<StartupController> logger)
    {
        _config = config;
        _userManager = userManager;
        _localizationManager = localizationManager;
        _logger = logger;
    }

    /// <summary>
    /// Completes the startup wizard.
    /// </summary>
    /// <response code="204">Startup wizard completed.</response>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    [HttpPost("Complete")]
    [Authorize(Policy = Policies.AnonymousLanAccessPolicy)]
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
        var metadataCountryCode = _config.Configuration.MetadataCountryCode;
        if (string.IsNullOrWhiteSpace(metadataCountryCode))
        {
            metadataCountryCode = GetInstalledMetadataCountryCode();
        }
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
    [Authorize(Policy = Policies.AnonymousLanAccessPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Obsolete("Use configuration endpoints")]
    public ActionResult UpdateInitialConfiguration([FromBody, Required] StartupConfigurationDto startupConfiguration)
    {
        _config.Configuration.ServerName = string.IsNullOrWhiteSpace(startupConfiguration.ServerName) ? DefaultServerName : startupConfiguration.ServerName;
        _config.Configuration.UICulture = startupConfiguration.UICulture ?? string.Empty;
        var metadataCountryCode = startupConfiguration.MetadataCountryCode;
        if (string.IsNullOrWhiteSpace(metadataCountryCode))
        {
            metadataCountryCode = GetInstalledMetadataCountryCode();
        }
        _config.Configuration.MetadataCountryCode = metadataCountryCode;
        _config.Configuration.PreferredMetadataLanguage = string.Equals(metadataCountryCode, "BR", StringComparison.OrdinalIgnoreCase)
            ? _localizationManager.GetDefaultMetadataLanguage(metadataCountryCode)
            : string.IsNullOrWhiteSpace(startupConfiguration.PreferredMetadataLanguage)
            ? _localizationManager.GetDefaultMetadataLanguage(metadataCountryCode)
            : startupConfiguration.PreferredMetadataLanguage;
        _config.SaveConfiguration();
        return NoContent();
    }

    private static string GetInstalledMetadataCountryCode()
    {
        try
        {
            return new RegionInfo(CultureInfo.InstalledUICulture.Name).TwoLetterISORegionName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Sets remote access and UPnP.
    /// </summary>
    /// <param name="startupRemoteAccessDto">The startup remote access dto.</param>
    /// <response code="204">Configuration saved.</response>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    [HttpPost("RemoteAccess")]
    [Authorize(Policy = Policies.AnonymousLanAccessPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Obsolete("Use configuration endpoints")]
    public ActionResult SetRemoteAccess([FromBody, Required] StartupRemoteAccessDto startupRemoteAccessDto)
    {
        NetworkConfiguration settings = _config.GetNetworkConfiguration();
        settings.EnableRemoteAccess = startupRemoteAccessDto.EnableRemoteAccess;
        settings.EnableUPnP = startupRemoteAccessDto.EnableRemoteAccess;
        settings.EnablePublishedServerUriByRequest = startupRemoteAccessDto.EnableRemoteAccess;
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
    [Authorize(Policy = Policies.AnonymousLanAccessPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> UpdateStartupUser([FromBody] StartupUserDto startupUserDto)
    {
        try
        {
            await _userManager.InitializeAsync().ConfigureAwait(false);

            var user = _userManager.GetFirstUser();
            if (user is null)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(startupUserDto.Password))
            {
                return BadRequest("Password must not be empty");
            }

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update startup user.");
            throw;
        }
    }
}
