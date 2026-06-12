using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture.Xunit3;
using MulletaFlix.Api.Controllers;
using MulletaFlix.Api.Models.UserDtos;
using MulletaFlix.Api.Results;
using MulletaFlix.Data;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Server.Implementations.Users;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.QuickConnect;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Nikse.SubtitleEdit.Core.Common;
using Xunit;

namespace MulletaFlix.Api.Tests.Controllers;

public class UserControllerTests
{
    private readonly UserController _subject;
    private readonly Mock<IUserManager> _mockUserManager;
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly Mock<INetworkManager> _mockNetworkManager;
    private readonly Mock<IDeviceManager> _mockDeviceManager;
    private readonly Mock<IAuthorizationContext> _mockAuthorizationContext;
    private readonly Mock<IServerConfigurationManager> _mockServerConfigurationManager;
    private readonly Mock<ILogger<UserController>> _mockLogger;
    private readonly Mock<IQuickConnect> _mockQuickConnect;
    private readonly Mock<IPlaylistManager> _mockPlaylistManager;
    private readonly Mock<IUserLicenseManager> _mockUserLicenseManager;

    public UserControllerTests()
    {
        _mockUserManager = new Mock<IUserManager>();
        _mockSessionManager = new Mock<ISessionManager>();
        _mockNetworkManager = new Mock<INetworkManager>();
        _mockDeviceManager = new Mock<IDeviceManager>();
        _mockAuthorizationContext = new Mock<IAuthorizationContext>();
        _mockServerConfigurationManager = new Mock<IServerConfigurationManager>();
        _mockLogger = new Mock<ILogger<UserController>>();
        _mockQuickConnect = new Mock<IQuickConnect>();
        _mockPlaylistManager = new Mock<IPlaylistManager>();
        _mockUserLicenseManager = new Mock<IUserLicenseManager>();

        _subject = new UserController(
            _mockUserManager.Object,
            _mockSessionManager.Object,
            _mockNetworkManager.Object,
            _mockDeviceManager.Object,
            _mockAuthorizationContext.Object,
            _mockServerConfigurationManager.Object,
            _mockLogger.Object,
            _mockQuickConnect.Object,
            _mockPlaylistManager.Object,
            _mockUserLicenseManager.Object);
    }

    [Theory]
    [AutoData]
    public async Task UpdateUserPolicy_WhenUserNotFound_ReturnsNotFound(Guid userId, UserPolicy userPolicy)
    {
        User? nullUser = null;
        _mockUserManager.
            Setup(m => m.GetUserById(userId))
            .Returns(nullUser);

        Assert.IsType<NotFoundResult>(await _subject.UpdateUserPolicy(userId, userPolicy));
    }

    [Fact]
    public async Task RegisterUser_WhenSuccessful_CreatesDisabledUser()
    {
        const string username = "newuser@example.com";
        const string password = "password123";

        var createdUser = new User(
            username,
            typeof(DefaultAuthenticationProvider).FullName!,
            typeof(DefaultPasswordResetProvider).FullName!);
        createdUser.AddDefaultPermissions();
        createdUser.AddDefaultPreferences();

        _mockUserManager
            .Setup(m => m.CreateUserAsync(username))
            .ReturnsAsync(createdUser);

        _mockUserManager
            .Setup(m => m.ChangePassword(createdUser.Id, password))
            .Returns(Task.CompletedTask);

        _mockUserManager
            .Setup(m => m.GetUserDto(createdUser, It.IsAny<string?>()))
            .Returns(new UserDto
            {
                Policy = new UserPolicy
                {
                    AuthenticationProviderId = createdUser.AuthenticationProviderId,
                    PasswordResetProviderId = createdUser.PasswordResetProviderId,
                    IsHidden = true,
                    IsDisabled = false
                }
            });

        UserPolicy? capturedPolicy = null;
        _mockUserManager
            .Setup(m => m.UpdatePolicyAsync(createdUser.Id, It.IsAny<UserPolicy>()))
            .Callback<Guid, UserPolicy>((_, policy) => capturedPolicy = policy)
            .Returns(Task.CompletedTask);

        var result = await _subject.RegisterUser(new CreateUserByName
        {
            Name = username,
            Password = password
        });

        var okResult = Assert.IsType<OkResult<RegisterUserResult>>(result.Result);
        var payload = Assert.IsType<RegisterUserResult>(okResult.Value);
        Assert.True(payload.Success);

        Assert.NotNull(capturedPolicy);
        Assert.True(capturedPolicy!.IsHidden);
        Assert.False(capturedPolicy.IsDisabled);

        _mockUserManager.Verify(m => m.CreateUserAsync(username), Times.Once);
        _mockUserManager.Verify(m => m.ChangePassword(createdUser.Id, password), Times.Once);
        _mockUserManager.Verify(m => m.UpdatePolicyAsync(createdUser.Id, It.IsAny<UserPolicy>()), Times.Once);
    }

    [Fact]
    public async Task RegisterUser_WhenUsernameTaken_ReturnsBadRequest()
    {
        const string username = "existing@example.com";

        _mockUserManager
            .Setup(m => m.CreateUserAsync(username))
            .ThrowsAsync(new ArgumentException($"A user with the name '{username}' already exists."));

        var result = await _subject.RegisterUser(new CreateUserByName
        {
            Name = username,
            Password = "password123"
        });

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var payload = Assert.IsType<RegisterUserResult>(badRequestResult.Value);
        Assert.False(payload.Success);
        Assert.Equal("MessageRegisterUsernameTaken", payload.Message);
    }

    [Theory]
    [InlineAutoData(null)]
    [InlineAutoData("")]
    [InlineAutoData("   ")]
    public void UpdateUserPolicy_WhenPasswordResetProviderIdNotSupplied_ReturnsBadRequest(string? passwordResetProviderId)
    {
        var userPolicy = new UserPolicy
        {
            PasswordResetProviderId = passwordResetProviderId,
            AuthenticationProviderId = "AuthenticationProviderId"
        };

        Assert.Contains(
            Validate(userPolicy), v =>
                v.MemberNames.Contains("PasswordResetProviderId") &&
                v.ErrorMessage is not null &&
                v.ErrorMessage.Contains("required", StringComparison.CurrentCultureIgnoreCase));
    }

    [Theory]
    [InlineAutoData(null)]
    [InlineAutoData("")]
    [InlineAutoData("   ")]
    public void UpdateUserPolicy_WhenAuthenticationProviderIdNotSupplied_ReturnsBadRequest(string? authenticationProviderId)
    {
        var userPolicy = new UserPolicy
        {
            AuthenticationProviderId = authenticationProviderId,
            PasswordResetProviderId = "PasswordResetProviderId"
        };

        Assert.Contains(Validate(userPolicy), v =>
            v.MemberNames.Contains("AuthenticationProviderId") &&
            v.ErrorMessage is not null &&
            v.ErrorMessage.Contains("required", StringComparison.CurrentCultureIgnoreCase));
    }

    private List<ValidationResult> Validate(object model)
    {
        var result = new List<ValidationResult>();
        var context = new ValidationContext(model, null, null);
        Validator.TryValidateObject(model, context, result, true);

        return result;
    }
}

