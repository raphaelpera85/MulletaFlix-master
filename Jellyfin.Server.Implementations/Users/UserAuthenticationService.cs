using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Data;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.Contexts;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Database.Implementations.Enums;
using MulletaFlix.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Model.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Server.Implementations.Users
{
    internal sealed class UserAuthenticationService
    {
        private readonly IDbContextFactory<UsersDbContext> _dbProvider;
        private readonly IReadOnlyCollection<IAuthenticationProvider> _authenticationProviders;
        private readonly IAuthenticationProvider _invalidAuthProvider;
        private readonly IAuthenticationProvider _defaultAuthenticationProvider;
        private readonly ILogger _logger;
        private readonly UserManager _userManager;
        private readonly INetworkManager _networkManager;

        public UserAuthenticationService(
            IDbContextFactory<UsersDbContext> dbProvider,
            IReadOnlyCollection<IAuthenticationProvider> authenticationProviders,
            IAuthenticationProvider invalidAuthProvider,
            IAuthenticationProvider defaultAuthenticationProvider,
            ILogger logger,
            UserManager userManager,
            INetworkManager networkManager)
        {
            _dbProvider = dbProvider;
            _authenticationProviders = authenticationProviders;
            _invalidAuthProvider = invalidAuthProvider;
            _defaultAuthenticationProvider = defaultAuthenticationProvider;
            _logger = logger;
            _userManager = userManager;
            _networkManager = networkManager;
        }

        public async Task<User?> AuthenticateUser(
            string username,
            string password,
            string remoteEndPoint,
            bool isUserSession)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                _logger.LogInformation("Authentication request without username has been denied (IP: {IP}).", remoteEndPoint);
                throw new ArgumentNullException(nameof(username));
            }

            bool success;
            var user = _userManager.GetUserByName(username);
            using (await _userManager.UserLock.LockAsync(user?.Id ?? GetLockIdForUsername(username)).ConfigureAwait(false))
            {
                // Reload the user now that we hold the lock so the RowVersion is current.
                if (user is not null)
                {
                    user = _userManager.GetUserById(user.Id) ?? user;
                }

                var authResult = await AuthenticateLocalUser(username, password, user).ConfigureAwait(false);
                var authenticationProvider = authResult.AuthenticationProvider;
                success = authResult.Success;

                if (user is null)
                {
                    string updatedUsername = authResult.Username;

                    if (success
                        && authenticationProvider is not null
                        && authenticationProvider is not DefaultAuthenticationProvider)
                    {
                        // Trust the username returned by the authentication provider
                        username = updatedUsername;

                        // Search the database for the user again
                        // the authentication provider might have created it
                        user = _userManager.GetUserByName(username);

                        if (authenticationProvider is IHasNewUserPolicy hasNewUserPolicy && user is not null)
                        {
                            await _userManager.UpdatePolicyAsync(user.Id, hasNewUserPolicy.GetNewUserPolicy()).ConfigureAwait(false);
                        }
                    }
                }

                if (success && user is not null && authenticationProvider is not null)
                {
                    var providerId = authenticationProvider.GetType().FullName;

                    if (providerId is not null && !string.Equals(providerId, user.AuthenticationProviderId, StringComparison.OrdinalIgnoreCase))
                    {
                        user.AuthenticationProviderId = providerId;
                        await _userManager.UpdateUserInternalAsync(user).ConfigureAwait(false);
                    }
                }

                if (user is null)
                {
                    _logger.LogInformation(
                        "Authentication request for {UserName} has been denied (IP: {IP}).",
                        username,
                        remoteEndPoint);
                    throw new AuthenticationException("Invalid username or password entered.");
                }

                // Check for expired license only during login.
                if (!user.HasPermission(PermissionKind.IsAdministrator))
                {
                    var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
                    await using (dbContext.ConfigureAwait(false))
                    {
                        var license = await dbContext.UserLicenses
                            .AsNoTracking()
                            .FirstOrDefaultAsync(l => l.UserId.Equals(user.Id))
                            .ConfigureAwait(false);

                        if (license is not null && !license.IsUnlimited
                            && license.ExpirationDate.HasValue
                            && license.ExpirationDate.Value < DateTime.UtcNow)
                        {
                            _logger.LogInformation(
                                "Authentication request for {UserName} denied: license expired at {ExpirationDate} (IP: {IP}).",
                                username,
                                license.ExpirationDate.Value,
                                remoteEndPoint);
                            throw new System.Security.SecurityException(
                                $"A licença de acesso de {user.Username} expirou em {license.ExpirationDate.Value:dd/MM/yyyy HH:mm}. Entre em contato com o administrador.");
                        }
                    }
                }

                if (user.HasPermission(PermissionKind.IsDisabled))
                {
                    _logger.LogInformation(
                        "Authentication request for {UserName} has been denied because this account is currently disabled (IP: {IP}).",
                        username,
                        remoteEndPoint);
                    throw new System.Security.SecurityException(
                        $"The {user.Username} account is currently disabled. Please consult with your administrator.");
                }

                if (!user.HasPermission(PermissionKind.EnableRemoteAccess) &&
                    !_networkManager.IsInLocalNetwork(remoteEndPoint))
                {
                    _logger.LogInformation(
                        "Authentication request for {UserName} forbidden: remote access disabled and user not in local network (IP: {IP}).",
                        username,
                        remoteEndPoint);
                    throw new System.Security.SecurityException("Forbidden.");
                }

                if (!user.IsParentalScheduleAllowed())
                {
                    _logger.LogInformation(
                        "Authentication request for {UserName} is not allowed at this time due parental restrictions (IP: {IP}).",
                        username,
                        remoteEndPoint);
                    throw new System.Security.SecurityException("User is not allowed access at this time.");
                }

                // Update LastActivityDate and LastLoginDate, then save
                if (success)
                {
                    if (isUserSession)
                    {
                        user.LastActivityDate = user.LastLoginDate = DateTime.UtcNow;
                    }

                    user.InvalidLoginAttemptCount = 0;
                    await _userManager.UpdateUserInternalAsync(user).ConfigureAwait(false);
                    _logger.LogInformation("Authentication request for {UserName} has succeeded.", user.Username);
                }
                else
                {
                    await _userManager.IncrementInvalidLoginAttemptCount(user).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Authentication request for {UserName} has been denied (IP: {IP}).",
                        user.Username,
                        remoteEndPoint);
                }
            }

            return success ? user : null;
        }

        public NameIdPair[] GetAuthenticationProviders()
        {
            return _authenticationProviders
                .Where(provider => provider.IsEnabled)
                .OrderBy(i => i is DefaultAuthenticationProvider ? 0 : 1)
                .ThenBy(i => i.Name)
                .Select(i => new NameIdPair
                {
                    Name = i.Name,
                    Id = i.GetType().FullName
                })
                .ToArray();
        }

        public IAuthenticationProvider GetAuthenticationProvider(User user)
        {
            return GetAuthenticationProviders(user)[0];
        }

        public List<IAuthenticationProvider> GetAuthenticationProviders(User? user)
        {
            var authenticationProviderId = user?.AuthenticationProviderId;

            var providers = _authenticationProviders.Where(i => i.IsEnabled).ToList();

            if (!string.IsNullOrEmpty(authenticationProviderId))
            {
                providers = providers.Where(i => string.Equals(authenticationProviderId, i.GetType().FullName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (providers.Count == 0)
            {
                _logger.LogWarning(
                    "User {Username} was found with invalid/missing Authentication Provider {AuthenticationProviderId}. Assigning user to InvalidAuthProvider until this is corrected",
                    user?.Username,
                    user?.AuthenticationProviderId);
                providers = new List<IAuthenticationProvider>
                {
                    _invalidAuthProvider
                };
            }

            return providers;
        }

        private async Task<(IAuthenticationProvider? AuthenticationProvider, string Username, bool Success)> AuthenticateLocalUser(
            string username,
            string password,
            User? user)
        {
            bool success = false;
            IAuthenticationProvider? authenticationProvider = null;

            foreach (var provider in GetAuthenticationProviders(user))
            {
                var providerAuthResult =
                    await AuthenticateWithProvider(provider, username, password, user).ConfigureAwait(false);
                var updatedUsername = providerAuthResult.Username;
                success = providerAuthResult.Success;

                if (success)
                {
                    authenticationProvider = provider;
                    username = updatedUsername;
                    break;
                }
            }

            return (authenticationProvider, username, success);
        }

        private async Task<(string Username, bool Success)> AuthenticateWithProvider(
            IAuthenticationProvider provider,
            string username,
            string password,
            User? resolvedUser)
        {
            try
            {
                var authenticationResult = provider is IRequiresResolvedUser requiresResolvedUser
                    ? await requiresResolvedUser.Authenticate(username, password, resolvedUser).ConfigureAwait(false)
                    : await provider.Authenticate(username, password).ConfigureAwait(false);

                if (authenticationResult.Username != username)
                {
                    _logger.LogDebug("Authentication provider provided updated username {1}", authenticationResult.Username);
                    username = authenticationResult.Username;
                }

                return (username, true);
            }
            catch (AuthenticationException ex)
            {
                _logger.LogDebug(ex, "Error authenticating with provider {Provider}", provider.Name);

                return (username, false);
            }
        }

        private static Guid GetLockIdForUsername(string username)
        {
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(username.ToUpperInvariant()));
            return new Guid(hash);
        }
    }
}
