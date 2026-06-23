#pragma warning disable RS0030 // Do not use banned APIs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AsyncKeyedLock;
using MediaBrowser.Common;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using MulletaFlix.Data;
using MulletaFlix.Data.Enums;
using MulletaFlix.Data.Events;
using MulletaFlix.Data.Events.Users;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.Contexts;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Database.Implementations.Enums;
using MulletaFlix.Extensions;

namespace MulletaFlix.Server.Implementations.Users
{
    /// <summary>
    /// Manages the creation and retrieval of <see cref="User"/> instances.
    /// </summary>
    public partial class UserManager : IUserManager, IDisposable
    {
        private readonly IDbContextFactory<UsersDbContext> _dbProvider;
        private readonly IEventManager _eventManager;
        private readonly INetworkManager _networkManager;
        private readonly IApplicationHost _appHost;
        private readonly IImageProcessor _imageProcessor;
        private readonly ILogger<UserManager> _logger;
        private readonly IReadOnlyCollection<IPasswordResetProvider> _passwordResetProviders;
        private readonly IReadOnlyCollection<IAuthenticationProvider> _authenticationProviders;
        private readonly InvalidAuthProvider _invalidAuthProvider;
        private readonly DefaultAuthenticationProvider _defaultAuthenticationProvider;
        private readonly DefaultPasswordResetProvider _defaultPasswordResetProvider;
        private readonly IServerConfigurationManager _serverConfigurationManager;

        private readonly AsyncKeyedLocker<Guid> _userLock = new();
        private readonly SemaphoreSlim _schemaInitializationLock = new(1, 1);
        private bool _schemaInitialized;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserManager"/> class.
        /// </summary>
        /// <param name="dbProvider">The database provider.</param>
        /// <param name="eventManager">The event manager.</param>
        /// <param name="networkManager">The network manager.</param>
        /// <param name="appHost">The application host.</param>
        /// <param name="imageProcessor">The image processor.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="serverConfigurationManager">The system config manager.</param>
        /// <param name="passwordResetProviders">The password reset providers.</param>
        /// <param name="authenticationProviders">The authentication providers.</param>
        public UserManager(
            IDbContextFactory<UsersDbContext> dbProvider,
            IEventManager eventManager,
            INetworkManager networkManager,
            IApplicationHost appHost,
            IImageProcessor imageProcessor,
            ILogger<UserManager> logger,
            IServerConfigurationManager serverConfigurationManager,
            IEnumerable<IPasswordResetProvider> passwordResetProviders,
            IEnumerable<IAuthenticationProvider> authenticationProviders)
        {
            _dbProvider = dbProvider;
            _eventManager = eventManager;
            _networkManager = networkManager;
            _appHost = appHost;
            _imageProcessor = imageProcessor;
            _logger = logger;
            _serverConfigurationManager = serverConfigurationManager;

            _passwordResetProviders = passwordResetProviders.ToList();
            _authenticationProviders = authenticationProviders.ToList();

            _invalidAuthProvider = _authenticationProviders.OfType<InvalidAuthProvider>().First();
            _defaultAuthenticationProvider = _authenticationProviders.OfType<DefaultAuthenticationProvider>().First();
            _defaultPasswordResetProvider = _passwordResetProviders.OfType<DefaultPasswordResetProvider>().First();
        }

        /// <inheritdoc/>
        public event EventHandler<GenericEventArgs<User>>? OnUserUpdated;

        /// <inheritdoc/>
        public IEnumerable<User> GetUsers()
        {
            EnsureSchemaCreatedAsync().GetAwaiter().GetResult();
            using var dbContext = _dbProvider.CreateDbContext();
            return UserQuery(dbContext)
                .ToArray();
        }

        /// <inheritdoc/>
        public IEnumerable<Guid> GetUsersIds()
        {
            EnsureSchemaCreatedAsync().GetAwaiter().GetResult();
            using var dbContext = _dbProvider.CreateDbContext();
            return dbContext.Users
                .AsNoTracking()
                .Select(user => user.Id)
                .ToArray();
        }

        // This is some regex that matches only on unicode "word" characters, as well as -, _ and @
        // In theory this will cut out most if not all 'control' characters which should help minimize any weirdness
        // Usernames can contain letters (a-z + whatever else unicode is cool with), numbers (0-9), at-signs (@), dashes (-), underscores (_), apostrophes ('), periods (.) and spaces ( )
        [GeneratedRegex(@"^(?!\s)[\p{L}\p{N}\ \-'._@+]+(?<!\s)$")]
        private static partial Regex ValidUsernameRegex();

        /// <inheritdoc/>
        public User? GetUserById(Guid id)
        {
            if (id.IsEmpty())
            {
                throw new ArgumentException("Guid can't be empty", nameof(id));
            }

            EnsureSchemaCreatedAsync().GetAwaiter().GetResult();
            using var dbContext = _dbProvider.CreateDbContext();
            return UserQuery(dbContext)
                .FirstOrDefault(user => user.Id == id);
        }

        private static IQueryable<User> UserQuery(UsersDbContext dbContext)
        {
            return dbContext.Users
                            .AsSplitQuery()
                            .Include(user => user.Permissions)
                            .Include(user => user.Preferences)
                            .Include(user => user.AccessSchedules)
                            .Include(user => user.ProfileImage)
                            .AsNoTracking();
        }

        private async Task EnsureSchemaCreatedAsync(bool force = false)
        {
            if (_schemaInitialized && !force)
            {
                return;
            }

            await _schemaInitializationLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_schemaInitialized && !force)
                {
                    return;
                }

                var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
                await using (dbContext.ConfigureAwait(false))
                {
                    _logger.LogInformation("Ensuring user schema is available for provider {Provider}.", dbContext.Database.ProviderName);
                    var databaseCreator = dbContext.Database.GetService<IDatabaseCreator>() as IRelationalDatabaseCreator
                        ?? throw new InvalidOperationException("User management requires a relational database provider.");

                    if (!await databaseCreator.ExistsAsync().ConfigureAwait(false))
                    {
                        _logger.LogInformation("User database does not exist yet; creating it first.");
                        await databaseCreator.CreateAsync().ConfigureAwait(false);
                    }

                    await EnsureUserSchemaTablesAsync(dbContext).ConfigureAwait(false);
                    _logger.LogInformation("User schema bootstrap completed for provider {Provider}.", dbContext.Database.ProviderName);
                }

                _schemaInitialized = true;
            }
            finally
            {
                _schemaInitializationLock.Release();
            }
        }

        /// <inheritdoc/>
        public User? GetFirstUser()
        {
            try
            {
                EnsureSchemaCreatedAsync(force: true).GetAwaiter().GetResult();
                using var dbContext = _dbProvider.CreateDbContext();
                return UserQuery(dbContext).FirstOrDefault();
            }
            catch (Exception ex) when (IsMissingTableException(ex))
            {
                EnsureSchemaCreatedAsync(force: true).GetAwaiter().GetResult();
                using var dbContext = _dbProvider.CreateDbContext();
                return UserQuery(dbContext).FirstOrDefault();
            }
        }

        /// <inheritdoc/>
        public User? GetUserByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Invalid username", nameof(name));
            }

            EnsureSchemaCreatedAsync().GetAwaiter().GetResult();
            using var dbContext = _dbProvider.CreateDbContext();
#pragma warning disable CA1862 // Use the 'StringComparison' method overloads to perform case-insensitive string comparisons
            return UserQuery(dbContext)
                .FirstOrDefault(u => u.NormalizedUsername == name.ToUpperInvariant());
#pragma warning restore CA1862 // Use the 'StringComparison' method overloads to perform case-insensitive string comparisons
        }

        /// <inheritdoc/>
        public async Task RenameUser(Guid userId, string oldName, string newName)
        {
            await EnsureSchemaCreatedAsync().ConfigureAwait(false);
            ThrowIfInvalidUsername(newName);

            if (oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The new and old names must be different.");
            }

            User user = null!; // user is never actually null where its used afterwards so we can just ignore.
            using (await _userLock.LockAsync(userId).ConfigureAwait(false))
            {
                var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
                await using (dbContext.ConfigureAwait(false))
                {
#pragma warning disable CA1862 // Use the 'StringComparison' method overloads to perform case-insensitive string comparisons
                    if (await dbContext.Users
                            .AnyAsync(u => u.NormalizedUsername == newName.ToUpperInvariant() && u.Id != userId)
                            .ConfigureAwait(false))
                    {
                        throw new ArgumentException(string.Format(
                            CultureInfo.InvariantCulture,
                            "A user with the name '{0}' already exists.",
                            newName));
                    }
#pragma warning restore CA1862 // Use the 'StringComparison' method overloads to perform case-insensitive string comparisons

                    user = await UserQuery(dbContext)
                        .AsTracking()
                        .FirstOrDefaultAsync(u => u.Id == userId)
                        .ConfigureAwait(false)
                        ?? throw new ResourceNotFoundException(nameof(userId));
                    user.Username = newName;
                    user.NormalizedUsername = newName.ToUpperInvariant();
                    await UpdateUserInternalAsync(dbContext, user).ConfigureAwait(false);
                }
            }

            var eventArgs = new UserUpdatedEventArgs(user);
            await _eventManager.PublishAsync(eventArgs).ConfigureAwait(false);
            OnUserUpdated?.Invoke(this, eventArgs);
        }

        /// <inheritdoc/>
        public async Task UpdateUserAsync(User user)
        {
            using (await _userLock.LockAsync(user.Id).ConfigureAwait(false))
            {
                await UpdateUserInternalAsync(user).ConfigureAwait(false);
            }
        }

        internal async Task<User> CreateUserInternalAsync(string name, UsersDbContext dbContext)
        {
            // TODO: Remove after user item data is migrated.
            var max = await dbContext.Users.AsQueryable().AnyAsync().ConfigureAwait(false)
                ? await dbContext.Users.AsQueryable().Select(u => u.InternalId).MaxAsync().ConfigureAwait(false)
                : 0;

            var user = new User(
                name,
                _defaultAuthenticationProvider.GetType().FullName!,
                _defaultPasswordResetProvider.GetType().FullName!)
            {
                InternalId = max + 1
            };

            user.AddDefaultPermissions();
            user.AddDefaultPreferences();
            user.SetPermission(PermissionKind.IsHidden, true);

            return user;
        }

        /// <inheritdoc/>
        public async Task<User> CreateUserAsync(string name)
        {
            await EnsureSchemaCreatedAsync().ConfigureAwait(false);
            ThrowIfInvalidUsername(name);

            User newUser;
            var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
            await using (dbContext.ConfigureAwait(false))
            {
#pragma warning disable CA1862 // Use the 'StringComparison' method overloads to perform case-insensitive string comparisons
                if (await dbContext.Users
                        .AnyAsync(u => u.NormalizedUsername == name.ToUpperInvariant())
                        .ConfigureAwait(false))
                {
                    throw new ArgumentException(string.Format(
                        CultureInfo.InvariantCulture,
                        "A user with the name '{0}' already exists.",
                        name));
                }
#pragma warning restore CA1862 // Use the 'StringComparison' method overloads to perform case-insensitive string comparisons

                newUser = await CreateUserInternalAsync(name, dbContext).ConfigureAwait(false);

                dbContext.Users.Add(newUser);
                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }

            await _eventManager.PublishAsync(new UserCreatedEventArgs(newUser)).ConfigureAwait(false);

            return newUser;
        }

        /// <inheritdoc/>
        public async Task DeleteUserAsync(Guid userId)
        {
            await EnsureSchemaCreatedAsync().ConfigureAwait(false);
            User? user;
            using (await _userLock.LockAsync(userId).ConfigureAwait(false))
            {
                var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
                await using (dbContext.ConfigureAwait(false))
                {
                    user = await dbContext.Users
                        .Include(u => u.Permissions)
                        .FirstOrDefaultAsync(u => u.Id.Equals(userId))
                        .ConfigureAwait(false);
                    if (user is null)
                    {
                        throw new ResourceNotFoundException(nameof(userId));
                    }

                    var userCount = await dbContext.Users.CountAsync().ConfigureAwait(false);
                    if (userCount == 1)
                    {
                        throw new InvalidOperationException(string.Format(
                            CultureInfo.InvariantCulture,
                            "The user '{0}' cannot be deleted because there must be at least one user in the system.",
                            user.Username));
                    }

                    if (user.HasPermission(PermissionKind.IsAdministrator)
                        && await dbContext.Users
                            .CountAsync(i => i.Permissions.Any(p => p.Kind == PermissionKind.IsAdministrator && p.Value))
                            .ConfigureAwait(false) == 1)
                    {
                        throw new ArgumentException(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "The user '{0}' cannot be deleted because there must be at least one admin user in the system.",
                                user.Username),
                            nameof(userId));
                    }

                    dbContext.Users.Remove(user);
                    await dbContext.SaveChangesAsync().ConfigureAwait(false);
                }
            }

            await _eventManager.PublishAsync(new UserDeletedEventArgs(user)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public Task ResetPassword(Guid userId)
        {
            return ChangePassword(userId, string.Empty);
        }

        /// <inheritdoc/>
        public async Task ChangePassword(Guid userId, string newPassword)
        {
            await EnsureSchemaCreatedAsync().ConfigureAwait(false);
            User dbUser = null!;
            using (await _userLock.LockAsync(userId).ConfigureAwait(false))
            {
                var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
                await using (dbContext.ConfigureAwait(false))
                {
                    dbUser = await UserQuery(dbContext)
                        .AsTracking()
                        .FirstOrDefaultAsync(u => u.Id == userId)
                        .ConfigureAwait(false)
                        ?? throw new ResourceNotFoundException(nameof(userId));
                    if (dbUser.HasPermission(PermissionKind.IsAdministrator) && string.IsNullOrWhiteSpace(newPassword))
                    {
                        throw new ArgumentException("Admin user passwords must not be empty", nameof(newPassword));
                    }

                    await GetAuthenticationProvider(dbUser).ChangePassword(dbUser, newPassword).ConfigureAwait(false);
                    await dbContext.SaveChangesAsync().ConfigureAwait(false);
                }
            }

            await _eventManager.PublishAsync(new UserPasswordChangedEventArgs(dbUser)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public UserDto GetUserDto(User user, string? remoteEndPoint = null)
        {
            var castReceiverApplications = _serverConfigurationManager.Configuration.CastReceiverApplications;
            return new UserDto
            {
                Name = user.Username,
                Id = user.Id,
                ServerId = _appHost.SystemId,
                EnableAutoLogin = user.EnableAutoLogin,
                LastLoginDate = user.LastLoginDate,
                LastActivityDate = user.LastActivityDate,
                PrimaryImageTag = user.ProfileImage is not null ? _imageProcessor.GetImageCacheTag(user) : null,
                Configuration = new UserConfiguration
                {
                    SubtitleMode = user.SubtitleMode,
                    HidePlayedInLatest = user.HidePlayedInLatest,
                    EnableLocalPassword = user.EnableLocalPassword,
                    PlayDefaultAudioTrack = user.PlayDefaultAudioTrack,
                    DisplayCollectionsView = user.DisplayCollectionsView,
                    DisplayMissingEpisodes = user.DisplayMissingEpisodes,
                    AudioLanguagePreference = user.AudioLanguagePreference,
                    RememberAudioSelections = user.RememberAudioSelections,
                    EnableNextEpisodeAutoPlay = user.EnableNextEpisodeAutoPlay,
                    RememberSubtitleSelections = user.RememberSubtitleSelections,
                    SubtitleLanguagePreference = user.SubtitleLanguagePreference ?? string.Empty,
                    OrderedViews = user.GetPreferenceValues<Guid>(PreferenceKind.OrderedViews),
                    GroupedFolders = user.GetPreferenceValues<Guid>(PreferenceKind.GroupedFolders),
                    MyMediaExcludes = user.GetPreferenceValues<Guid>(PreferenceKind.MyMediaExcludes),
                    LatestItemsExcludes = user.GetPreferenceValues<Guid>(PreferenceKind.LatestItemExcludes),
                    CastReceiverId = string.IsNullOrEmpty(user.CastReceiverId)
                        ? castReceiverApplications.FirstOrDefault()?.Id
                        : castReceiverApplications.FirstOrDefault(c => string.Equals(c.Id, user.CastReceiverId, StringComparison.Ordinal))?.Id
                          ?? castReceiverApplications.FirstOrDefault()?.Id
                },
                Policy = new UserPolicy
                {
                    MaxParentalRating = user.MaxParentalRatingScore,
                    MaxParentalSubRating = user.MaxParentalRatingSubScore,
                    EnableUserPreferenceAccess = user.EnableUserPreferenceAccess,
                    RemoteClientBitrateLimit = user.RemoteClientBitrateLimit ?? 0,
                    AuthenticationProviderId = user.AuthenticationProviderId,
                    PasswordResetProviderId = user.PasswordResetProviderId,
                    InvalidLoginAttemptCount = user.InvalidLoginAttemptCount,
                    LoginAttemptsBeforeLockout = user.LoginAttemptsBeforeLockout ?? -1,
                    MaxActiveSessions = user.MaxActiveSessions,
                    IsAdministrator = user.HasPermission(PermissionKind.IsAdministrator),
                    IsHidden = user.HasPermission(PermissionKind.IsHidden),
                    IsDisabled = user.HasPermission(PermissionKind.IsDisabled),
                    EnableSharedDeviceControl = user.HasPermission(PermissionKind.EnableSharedDeviceControl),
                    EnableRemoteAccess = user.HasPermission(PermissionKind.EnableRemoteAccess),
                    EnableLiveTvManagement = user.HasPermission(PermissionKind.EnableLiveTvManagement),
                    EnableLiveTvAccess = user.HasPermission(PermissionKind.EnableLiveTvAccess),
                    EnableMediaPlayback = user.HasPermission(PermissionKind.EnableMediaPlayback),
                    EnableAudioPlaybackTranscoding = user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding),
                    EnableVideoPlaybackTranscoding = user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding),
                    EnableContentDeletion = user.HasPermission(PermissionKind.EnableContentDeletion),
                    EnableContentDownloading = user.HasPermission(PermissionKind.EnableContentDownloading),
                    EnableSyncTranscoding = user.HasPermission(PermissionKind.EnableSyncTranscoding),
                    EnableMediaConversion = user.HasPermission(PermissionKind.EnableMediaConversion),
                    EnableAllChannels = user.HasPermission(PermissionKind.EnableAllChannels),
                    EnableAllDevices = user.HasPermission(PermissionKind.EnableAllDevices),
                    EnableAllFolders = user.HasPermission(PermissionKind.EnableAllFolders),
                    EnableRemoteControlOfOtherUsers = user.HasPermission(PermissionKind.EnableRemoteControlOfOtherUsers),
                    EnablePlaybackRemuxing = user.HasPermission(PermissionKind.EnablePlaybackRemuxing),
                    ForceRemoteSourceTranscoding = user.HasPermission(PermissionKind.ForceRemoteSourceTranscoding),
                    EnablePublicSharing = user.HasPermission(PermissionKind.EnablePublicSharing),
                    EnableCollectionManagement = user.HasPermission(PermissionKind.EnableCollectionManagement),
                    EnableSubtitleManagement = user.HasPermission(PermissionKind.EnableSubtitleManagement),
                    AccessSchedules = user.AccessSchedules.ToArray(),
                    BlockedTags = user.GetPreference(PreferenceKind.BlockedTags),
                    AllowedTags = user.GetPreference(PreferenceKind.AllowedTags),
                    EnabledChannels = user.GetPreferenceValues<Guid>(PreferenceKind.EnabledChannels),
                    EnabledDevices = user.GetPreference(PreferenceKind.EnabledDevices),
                    EnabledFolders = user.GetPreferenceValues<Guid>(PreferenceKind.EnabledFolders),
                    EnableContentDeletionFromFolders = user.GetPreference(PreferenceKind.EnableContentDeletionFromFolders),
                    SyncPlayAccess = user.SyncPlayAccess,
                    BlockedChannels = user.GetPreferenceValues<Guid>(PreferenceKind.BlockedChannels),
                    BlockedMediaFolders = user.GetPreferenceValues<Guid>(PreferenceKind.BlockedMediaFolders),
                    BlockUnratedItems = user.GetPreferenceValues<UnratedItem>(PreferenceKind.BlockUnratedItems)
                }
            };
        }

        /// <inheritdoc/>
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
            var user = GetUserByName(username);
            using (await _userLock.LockAsync(user?.Id ?? Guid.Empty).ConfigureAwait(false))
            {
                // Reload the user now that we hold the lock so the RowVersion is current.
                // GetUserByName uses AsNoTracking and the snapshot may be stale if another
                // write (e.g. a concurrent login) incremented RowVersion after our initial load.
                if (user is not null)
                {
                    user = GetUserById(user.Id) ?? user;
                }

                var authResult = await AuthenticateLocalUser(username, password, user)
                    .ConfigureAwait(false);
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
                        user = GetUserByName(username);

                        if (authenticationProvider is IHasNewUserPolicy hasNewUserPolicy && user is not null)
                        {
                            await UpdatePolicyAsync(user.Id, hasNewUserPolicy.GetNewUserPolicy()).ConfigureAwait(false);
                        }
                    }
                }

                if (success && user is not null && authenticationProvider is not null)
                {
                    var providerId = authenticationProvider.GetType().FullName;

                    if (providerId is not null && !string.Equals(providerId, user.AuthenticationProviderId, StringComparison.OrdinalIgnoreCase))
                    {
                        user.AuthenticationProviderId = providerId;
                        await UpdateUserInternalAsync(user).ConfigureAwait(false);
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
                            throw new SecurityException(
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
                    throw new SecurityException(
                        $"The {user.Username} account is currently disabled. Please consult with your administrator.");
                }

                if (!user.HasPermission(PermissionKind.EnableRemoteAccess) &&
                    !_networkManager.IsInLocalNetwork(remoteEndPoint))
                {
                    _logger.LogInformation(
                        "Authentication request for {UserName} forbidden: remote access disabled and user not in local network (IP: {IP}).",
                        username,
                        remoteEndPoint);
                    throw new SecurityException("Forbidden.");
                }


                if (!user.IsParentalScheduleAllowed())
                {
                    _logger.LogInformation(
                        "Authentication request for {UserName} is not allowed at this time due parental restrictions (IP: {IP}).",
                        username,
                        remoteEndPoint);
                    throw new SecurityException("User is not allowed access at this time.");
                }

                // Update LastActivityDate and LastLoginDate, then save
                if (success)
                {
                    if (isUserSession)
                    {
                        user.LastActivityDate = user.LastLoginDate = DateTime.UtcNow;
                    }

                    user.InvalidLoginAttemptCount = 0;
                    await UpdateUserInternalAsync(user).ConfigureAwait(false);
                    _logger.LogInformation("Authentication request for {UserName} has succeeded.", user.Username);
                }
                else
                {
                    await IncrementInvalidLoginAttemptCount(user).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Authentication request for {UserName} has been denied (IP: {IP}).",
                        user.Username,
                        remoteEndPoint);
                }
            }

            return success ? user : null;
        }

        /// <inheritdoc/>
        public async Task<ForgotPasswordResult> StartForgotPasswordProcess(string enteredUsername, bool isInNetwork)
        {
            var user = string.IsNullOrWhiteSpace(enteredUsername) ? null : GetUserByName(enteredUsername);
            var passwordResetProvider = GetPasswordResetProvider(user);

            var result = await passwordResetProvider
                .StartForgotPasswordProcess(user, enteredUsername, isInNetwork)
                .ConfigureAwait(false);

            if (user is not null && isInNetwork)
            {
                await UpdateUserAsync(user).ConfigureAwait(false);
            }

            return result;
        }

        /// <inheritdoc/>
        public async Task<PinRedeemResult> RedeemPasswordResetPin(string pin)
        {
            foreach (var provider in _passwordResetProviders)
            {
                var result = await provider.RedeemPasswordResetPin(pin).ConfigureAwait(false);

                if (result.Success)
                {
                    return result;
                }
            }

            return new PinRedeemResult();
        }

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            await EnsureSchemaCreatedAsync(force: true).ConfigureAwait(false);

            // TODO: Refactor the startup wizard so that it doesn't require a user to already exist.
            var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
            await using (dbContext.ConfigureAwait(false))
            {
                try
                {
                    if (await dbContext.Users.AnyAsync().ConfigureAwait(false))
                    {
                        return;
                    }
                }
                catch (Exception ex) when (IsMissingTableException(ex))
                {
                    await EnsureSchemaCreatedAsync(force: true).ConfigureAwait(false);
                    if (await dbContext.Users.AnyAsync().ConfigureAwait(false))
                    {
                        return;
                    }
                }

                var defaultName = Environment.UserName;
                if (string.IsNullOrWhiteSpace(defaultName) || !ValidUsernameRegex().IsMatch(defaultName))
                {
                    defaultName = "MyMulletaFlixUser";
                }

                _logger.LogWarning("No users, creating one with username {UserName}", defaultName);

                var newUser = await CreateUserInternalAsync(defaultName, dbContext).ConfigureAwait(false);
                newUser.SetPermission(PermissionKind.IsAdministrator, true);
                newUser.SetPermission(PermissionKind.EnableContentDeletion, true);
                newUser.SetPermission(PermissionKind.EnableRemoteControlOfOtherUsers, true);

                dbContext.Users.Add(newUser);
                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public NameIdPair[] GetPasswordResetProviders()
        {
            return _passwordResetProviders
                .Where(provider => provider.IsEnabled)
                .OrderBy(i => i is DefaultPasswordResetProvider ? 0 : 1)
                .ThenBy(i => i.Name)
                .Select(i => new NameIdPair
                {
                    Name = i.Name,
                    Id = i.GetType().FullName
                })
                .ToArray();
        }

        /// <inheritdoc/>
        public async Task UpdateConfigurationAsync(Guid userId, UserConfiguration config)
        {
            await EnsureSchemaCreatedAsync().ConfigureAwait(false);
            using (await _userLock.LockAsync(userId).ConfigureAwait(false))
            {
                var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
                await using (dbContext.ConfigureAwait(false))
                {
                    var user = UserQuery(dbContext)
                                   .AsTracking()
                                   .FirstOrDefault(u => u.Id.Equals(userId))
                               ?? throw new ArgumentException("No user exists with given Id!");

                    user.SubtitleMode = config.SubtitleMode;
                    user.HidePlayedInLatest = config.HidePlayedInLatest;
                    user.EnableLocalPassword = config.EnableLocalPassword;
                    user.PlayDefaultAudioTrack = config.PlayDefaultAudioTrack;
                    user.DisplayCollectionsView = config.DisplayCollectionsView;
                    user.DisplayMissingEpisodes = config.DisplayMissingEpisodes;
                    user.AudioLanguagePreference = config.AudioLanguagePreference;
                    user.RememberAudioSelections = config.RememberAudioSelections;
                    user.EnableNextEpisodeAutoPlay = config.EnableNextEpisodeAutoPlay;
                    user.RememberSubtitleSelections = config.RememberSubtitleSelections;
                    user.SubtitleLanguagePreference = config.SubtitleLanguagePreference;

                    // Only set cast receiver id if it is passed in and it exists in the server config.
                    if (!string.IsNullOrEmpty(config.CastReceiverId)
                        && _serverConfigurationManager.Configuration.CastReceiverApplications.Any(c => string.Equals(c.Id, config.CastReceiverId, StringComparison.Ordinal)))
                    {
                        user.CastReceiverId = config.CastReceiverId;
                    }

                    user.SetPreference(PreferenceKind.OrderedViews, config.OrderedViews);
                    user.SetPreference(PreferenceKind.GroupedFolders, config.GroupedFolders);
                    user.SetPreference(PreferenceKind.MyMediaExcludes, config.MyMediaExcludes);
                    user.SetPreference(PreferenceKind.LatestItemExcludes, config.LatestItemsExcludes);

                    dbContext.Update(user);
                    await dbContext.SaveChangesAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task EnsureUserSchemaTablesAsync(UsersDbContext dbContext)
        {
            var providerName = dbContext.Database.ProviderName ?? string.Empty;
            var supportsSqlite = providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            var supportsMySql = providerName.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
                                providerName.Contains("MariaDb", StringComparison.OrdinalIgnoreCase);

            string[] statements = supportsSqlite
                ? [
                    """
                    CREATE TABLE IF NOT EXISTS "Users" (
                        "Id" TEXT NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY,
                        "Username" TEXT NOT NULL,
                        "NormalizedUsername" TEXT NOT NULL,
                        "Password" TEXT NULL,
                        "PhoneNumber" TEXT NULL,
                        "MustUpdatePassword" INTEGER NOT NULL,
                        "AudioLanguagePreference" TEXT NULL,
                        "AuthenticationProviderId" TEXT NOT NULL,
                        "PasswordResetProviderId" TEXT NOT NULL,
                        "InvalidLoginAttemptCount" INTEGER NOT NULL,
                        "LastActivityDate" TEXT NULL,
                        "LastLoginDate" TEXT NULL,
                        "LoginAttemptsBeforeLockout" INTEGER NULL,
                        "MaxActiveSessions" INTEGER NOT NULL,
                        "SubtitleMode" INTEGER NOT NULL,
                        "PlayDefaultAudioTrack" INTEGER NOT NULL,
                        "SubtitleLanguagePreference" TEXT NULL,
                        "DisplayMissingEpisodes" INTEGER NOT NULL,
                        "DisplayCollectionsView" INTEGER NOT NULL,
                        "EnableLocalPassword" INTEGER NOT NULL,
                        "HidePlayedInLatest" INTEGER NOT NULL,
                        "RememberAudioSelections" INTEGER NOT NULL,
                        "RememberSubtitleSelections" INTEGER NOT NULL,
                        "EnableNextEpisodeAutoPlay" INTEGER NOT NULL,
                        "EnableAutoLogin" INTEGER NOT NULL,
                        "EnableUserPreferenceAccess" INTEGER NOT NULL,
                        "MaxParentalRatingScore" INTEGER NULL,
                        "MaxParentalRatingSubScore" INTEGER NULL,
                        "RemoteClientBitrateLimit" INTEGER NULL,
                        "InternalId" INTEGER NOT NULL,
                        "SyncPlayAccess" INTEGER NOT NULL,
                        "CastReceiverId" TEXT NULL,
                        "RowVersion" INTEGER NOT NULL,
                        CONSTRAINT "AK_Users_Username" UNIQUE ("Username"),
                        CONSTRAINT "AK_Users_NormalizedUsername" UNIQUE ("NormalizedUsername")
                    );
                    """,
                    """
                    CREATE TABLE IF NOT EXISTS "ImageInfo" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_ImageInfo" PRIMARY KEY AUTOINCREMENT,
                        "UserId" TEXT NULL,
                        "Path" TEXT NOT NULL,
                        "LastModified" TEXT NOT NULL,
                        CONSTRAINT "AK_ImageInfo_UserId" UNIQUE ("UserId"),
                        CONSTRAINT "FK_ImageInfo_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
                    );
                    """,
                    """
                    CREATE TABLE IF NOT EXISTS "Permissions" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_Permissions" PRIMARY KEY AUTOINCREMENT,
                        "UserId" TEXT NULL,
                        "Kind" INTEGER NOT NULL,
                        "Value" INTEGER NOT NULL,
                        "RowVersion" INTEGER NOT NULL,
                        CONSTRAINT "FK_Permissions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE,
                        CONSTRAINT "AK_Permissions_UserId_Kind" UNIQUE ("UserId", "Kind")
                    );
                    """,
                    """
                    CREATE TABLE IF NOT EXISTS "Preferences" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_Preferences" PRIMARY KEY AUTOINCREMENT,
                        "UserId" TEXT NULL,
                        "Kind" INTEGER NOT NULL,
                        "Value" TEXT NOT NULL,
                        "RowVersion" INTEGER NOT NULL,
                        CONSTRAINT "FK_Preferences_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE,
                        CONSTRAINT "AK_Preferences_UserId_Kind" UNIQUE ("UserId", "Kind")
                    );
                    """,
                    """
                    CREATE TABLE IF NOT EXISTS "AccessSchedules" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_AccessSchedules" PRIMARY KEY AUTOINCREMENT,
                        "UserId" TEXT NOT NULL,
                        "DayOfWeek" INTEGER NOT NULL,
                        "StartHour" REAL NOT NULL,
                        "EndHour" REAL NOT NULL,
                        CONSTRAINT "FK_AccessSchedules_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
                    );
                    """,
                    """
                    CREATE TABLE IF NOT EXISTS "UserLicenses" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_UserLicenses" PRIMARY KEY AUTOINCREMENT,
                        "UserId" TEXT NOT NULL,
                        "StartDate" TEXT NOT NULL,
                        "DurationHours" INTEGER NULL,
                        "ExpirationDate" TEXT NULL,
                        "IsUnlimited" INTEGER NOT NULL,
                        "AdminNotes" TEXT NULL,
                        "GrantedByUserId" TEXT NULL,
                        "CreatedAt" TEXT NOT NULL,
                        "UpdatedAt" TEXT NOT NULL,
                        CONSTRAINT "AK_UserLicenses_UserId" UNIQUE ("UserId"),
                        CONSTRAINT "FK_UserLicenses_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
                    );
                    """
                ]
                : supportsMySql
                    ? [
                        """
                        CREATE TABLE IF NOT EXISTS `users` (
                            `Id` char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                            `Username` varchar(255) NOT NULL,
                            `NormalizedUsername` varchar(255) NOT NULL,
                            `Password` longtext NULL,
                            `PhoneNumber` varchar(20) NULL,
                            `MustUpdatePassword` tinyint(1) NOT NULL,
                            `AudioLanguagePreference` varchar(255) NULL,
                            `AuthenticationProviderId` varchar(255) NOT NULL,
                            `PasswordResetProviderId` varchar(255) NOT NULL,
                            `InvalidLoginAttemptCount` int NOT NULL,
                            `LastActivityDate` datetime(6) NULL,
                            `LastLoginDate` datetime(6) NULL,
                            `LoginAttemptsBeforeLockout` int NULL,
                            `MaxActiveSessions` int NOT NULL,
                            `SubtitleMode` int NOT NULL,
                            `PlayDefaultAudioTrack` tinyint(1) NOT NULL,
                            `SubtitleLanguagePreference` varchar(255) NULL,
                            `DisplayMissingEpisodes` tinyint(1) NOT NULL,
                            `DisplayCollectionsView` tinyint(1) NOT NULL,
                            `EnableLocalPassword` tinyint(1) NOT NULL,
                            `HidePlayedInLatest` tinyint(1) NOT NULL,
                            `RememberAudioSelections` tinyint(1) NOT NULL,
                            `RememberSubtitleSelections` tinyint(1) NOT NULL,
                            `EnableNextEpisodeAutoPlay` tinyint(1) NOT NULL,
                            `EnableAutoLogin` tinyint(1) NOT NULL,
                            `EnableUserPreferenceAccess` tinyint(1) NOT NULL,
                            `MaxParentalRatingScore` int NULL,
                            `MaxParentalRatingSubScore` int NULL,
                            `RemoteClientBitrateLimit` int NULL,
                            `InternalId` bigint NOT NULL,
                            `SyncPlayAccess` int NOT NULL,
                            `CastReceiverId` varchar(32) NULL,
                            `RowVersion` int unsigned NOT NULL,
                            CONSTRAINT `PK_Users` PRIMARY KEY (`Id`),
                            CONSTRAINT `AK_Users_Username` UNIQUE (`Username`),
                            CONSTRAINT `AK_Users_NormalizedUsername` UNIQUE (`NormalizedUsername`)
                        );
                        """,
                        """
                        CREATE TABLE IF NOT EXISTS `ImageInfo` (
                            `Id` int NOT NULL AUTO_INCREMENT,
                            `UserId` char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
                            `Path` varchar(512) NOT NULL,
                            `LastModified` datetime(6) NOT NULL,
                            CONSTRAINT `PK_ImageInfo` PRIMARY KEY (`Id`),
                            CONSTRAINT `AK_ImageInfo_UserId` UNIQUE (`UserId`),
                            CONSTRAINT `FK_ImageInfo_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
                        );
                        """,
                        """
                        CREATE TABLE IF NOT EXISTS `permissions` (
                            `Id` int NOT NULL AUTO_INCREMENT,
                            `UserId` char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
                            `Kind` int NOT NULL,
                            `Value` tinyint(1) NOT NULL,
                            `RowVersion` int unsigned NOT NULL,
                            CONSTRAINT `PK_mulletaflix_users_permissions` PRIMARY KEY (`Id`),
                            CONSTRAINT `AK_mulletaflix_users_permissions_UserId_Kind` UNIQUE (`UserId`, `Kind`),
                            CONSTRAINT `FK_mulletaflix_users_permissions_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
                        );
                        """,
                        """
                        CREATE TABLE IF NOT EXISTS `preferences` (
                            `Id` int NOT NULL AUTO_INCREMENT,
                            `UserId` char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
                            `Kind` int NOT NULL,
                            `Value` longtext NOT NULL,
                            `RowVersion` int unsigned NOT NULL,
                            CONSTRAINT `PK_mulletaflix_users_preferences` PRIMARY KEY (`Id`),
                            CONSTRAINT `AK_mulletaflix_users_preferences_UserId_Kind` UNIQUE (`UserId`, `Kind`),
                            CONSTRAINT `FK_mulletaflix_users_preferences_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
                        );
                        """,
                        """
                        CREATE TABLE IF NOT EXISTS `accessschedules` (
                            `Id` int NOT NULL AUTO_INCREMENT,
                            `UserId` char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                            `DayOfWeek` int NOT NULL,
                            `StartHour` double NOT NULL,
                            `EndHour` double NOT NULL,
                            CONSTRAINT `PK_mulletaflix_users_accessschedules` PRIMARY KEY (`Id`),
                            CONSTRAINT `FK_mulletaflix_users_accessschedules_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
                        );
                        """,
                        """
                        CREATE TABLE IF NOT EXISTS `userlicenses` (
                            `Id` int NOT NULL AUTO_INCREMENT,
                            `UserId` char(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                            `StartDate` datetime(6) NOT NULL,
                            `DurationHours` int NULL,
                            `ExpirationDate` datetime(6) NULL,
                            `IsUnlimited` tinyint(1) NOT NULL,
                            `AdminNotes` varchar(1024) NULL,
                            `GrantedByUserId` char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
                            `CreatedAt` datetime(6) NOT NULL,
                            `UpdatedAt` datetime(6) NOT NULL,
                            CONSTRAINT `PK_mulletaflix_users_userlicenses` PRIMARY KEY (`Id`),
                            CONSTRAINT `AK_mulletaflix_users_userlicenses_UserId` UNIQUE (`UserId`),
                            CONSTRAINT `FK_mulletaflix_users_userlicenses_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
                        );
                        """
                    ]
                    : throw new InvalidOperationException($"User management does not support provider '{providerName}'.");

            foreach (var statement in statements)
            {
                await dbContext.Database.ExecuteSqlRawAsync(statement, CancellationToken.None).ConfigureAwait(false);
            }

            // Compatibility section for legacy schema-prefixed tables is no longer needed.
            // All entities now use the default database directly with SchemaBehavior.Exclude.
        }

        private static bool IsMissingTableException(Exception exception)
        {
            for (var current = exception; current is not null; current = current.InnerException)
            {
                var message = current.Message;
                if (message.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("no such table", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("unknown table", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public async Task UpdatePolicyAsync(Guid userId, UserPolicy policy)
        {
            using (await _userLock.LockAsync(userId).ConfigureAwait(false))
            {
                var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
                await using (dbContext.ConfigureAwait(false))
                {
                    var user = UserQuery(dbContext)
                        .AsTracking()
                        .FirstOrDefault(u => u.Id.Equals(userId))
                        ?? throw new ArgumentException("No user exists with given Id!");

                    // The default number of login attempts is 3, but for some god forsaken reason it's sent to the server as "0"
                    int? maxLoginAttempts = policy.LoginAttemptsBeforeLockout switch
                    {
                        -1 => null,
                        0 => 3,
                        _ => policy.LoginAttemptsBeforeLockout
                    };

                    user.MaxParentalRatingScore = policy.MaxParentalRating;
                    user.MaxParentalRatingSubScore = policy.MaxParentalSubRating;
                    user.EnableUserPreferenceAccess = policy.EnableUserPreferenceAccess;
                    user.RemoteClientBitrateLimit = policy.RemoteClientBitrateLimit;
                    user.AuthenticationProviderId = policy.AuthenticationProviderId;
                    user.PasswordResetProviderId = policy.PasswordResetProviderId;
                    user.InvalidLoginAttemptCount = policy.InvalidLoginAttemptCount;
                    user.LoginAttemptsBeforeLockout = maxLoginAttempts;
                    user.MaxActiveSessions = policy.MaxActiveSessions;
                    user.SyncPlayAccess = policy.SyncPlayAccess;
                    user.SetPermission(PermissionKind.IsAdministrator, policy.IsAdministrator);
                    user.SetPermission(PermissionKind.IsHidden, policy.IsHidden);
                    user.SetPermission(PermissionKind.IsDisabled, policy.IsDisabled);
                    user.SetPermission(PermissionKind.EnableSharedDeviceControl, policy.EnableSharedDeviceControl);
                    user.SetPermission(PermissionKind.EnableRemoteAccess, policy.EnableRemoteAccess);
                    user.SetPermission(PermissionKind.EnableLiveTvManagement, policy.EnableLiveTvManagement);
                    user.SetPermission(PermissionKind.EnableLiveTvAccess, policy.EnableLiveTvAccess);
                    user.SetPermission(PermissionKind.EnableMediaPlayback, policy.EnableMediaPlayback);
                    user.SetPermission(PermissionKind.EnableAudioPlaybackTranscoding, policy.EnableAudioPlaybackTranscoding);
                    user.SetPermission(PermissionKind.EnableVideoPlaybackTranscoding, policy.EnableVideoPlaybackTranscoding);
                    user.SetPermission(PermissionKind.EnableContentDeletion, policy.EnableContentDeletion);
                    user.SetPermission(PermissionKind.EnableContentDownloading, policy.EnableContentDownloading);
                    user.SetPermission(PermissionKind.EnableSyncTranscoding, policy.EnableSyncTranscoding);
                    user.SetPermission(PermissionKind.EnableMediaConversion, policy.EnableMediaConversion);
                    user.SetPermission(PermissionKind.EnableAllChannels, policy.EnableAllChannels);
                    user.SetPermission(PermissionKind.EnableAllDevices, policy.EnableAllDevices);
                    user.SetPermission(PermissionKind.EnableAllFolders, policy.EnableAllFolders);
                    user.SetPermission(PermissionKind.EnableRemoteControlOfOtherUsers, policy.EnableRemoteControlOfOtherUsers);
                    user.SetPermission(PermissionKind.EnablePlaybackRemuxing, policy.EnablePlaybackRemuxing);
                    user.SetPermission(PermissionKind.EnableCollectionManagement, policy.EnableCollectionManagement);
                    user.SetPermission(PermissionKind.EnableSubtitleManagement, policy.EnableSubtitleManagement);
                    user.SetPermission(PermissionKind.EnableLyricManagement, policy.EnableLyricManagement);
                    user.SetPermission(PermissionKind.ForceRemoteSourceTranscoding, policy.ForceRemoteSourceTranscoding);
                    user.SetPermission(PermissionKind.EnablePublicSharing, policy.EnablePublicSharing);

                    user.AccessSchedules.Clear();
                    foreach (var policyAccessSchedule in policy.AccessSchedules)
                    {
                        user.AccessSchedules.Add(policyAccessSchedule);
                    }

                    // TODO: fix this at some point
                    user.SetPreference(PreferenceKind.BlockUnratedItems, policy.BlockUnratedItems ?? Array.Empty<UnratedItem>());
                    user.SetPreference(PreferenceKind.BlockedTags, policy.BlockedTags);
                    user.SetPreference(PreferenceKind.AllowedTags, policy.AllowedTags);
                    user.SetPreference(PreferenceKind.EnabledChannels, policy.EnabledChannels);
                    user.SetPreference(PreferenceKind.EnabledDevices, policy.EnabledDevices);
                    user.SetPreference(PreferenceKind.EnabledFolders, policy.EnabledFolders);
                    user.SetPreference(PreferenceKind.EnableContentDeletionFromFolders, policy.EnableContentDeletionFromFolders);

                    dbContext.Update(user);
                    await dbContext.SaveChangesAsync().ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc/>
        public async Task ClearProfileImageAsync(User user)
        {
            if (user.ProfileImage is null)
            {
                return;
            }

            using (await _userLock.LockAsync(user.Id).ConfigureAwait(false))
            {
                var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
                await using (dbContext.ConfigureAwait(false))
                {
                    dbContext.Remove(user.ProfileImage);
                    await dbContext.SaveChangesAsync().ConfigureAwait(false);
                }

                user.ProfileImage = null;
            }
        }

        internal static void ThrowIfInvalidUsername(string name)
        {
            if (!string.IsNullOrWhiteSpace(name) && IsValidUsername(name))
            {
                return;
            }

            throw new ArgumentException("Usernames can contain unicode symbols, numbers (0-9), dashes (-), underscores (_), apostrophes ('), and periods (.)", nameof(name));
        }

        private static bool IsValidUsername(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[^1]))
            {
                return false;
            }

            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '\'' || ch == '.' || ch == '_' || ch == '@' || ch == '+')
                {
                    continue;
                }

                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private IAuthenticationProvider GetAuthenticationProvider(User user)
        {
            return GetAuthenticationProviders(user)[0];
        }

        private IPasswordResetProvider GetPasswordResetProvider(User? user)
        {
            if (user is null)
            {
                return _defaultPasswordResetProvider;
            }

            return GetPasswordResetProviders(user)[0];
        }

        private List<IAuthenticationProvider> GetAuthenticationProviders(User? user)
        {
            var authenticationProviderId = user?.AuthenticationProviderId;

            var providers = _authenticationProviders.Where(i => i.IsEnabled).ToList();

            if (!string.IsNullOrEmpty(authenticationProviderId))
            {
                providers = providers.Where(i => string.Equals(authenticationProviderId, i.GetType().FullName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (providers.Count == 0)
            {
                // Assign the user to the InvalidAuthProvider since no configured auth provider was valid/found
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

        private IPasswordResetProvider[] GetPasswordResetProviders(User user)
        {
            var passwordResetProviderId = user.PasswordResetProviderId;
            var providers = _passwordResetProviders.Where(i => i.IsEnabled).ToArray();

            if (!string.IsNullOrEmpty(passwordResetProviderId))
            {
                providers = providers.Where(i =>
                        string.Equals(passwordResetProviderId, i.GetType().FullName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            if (providers.Length == 0)
            {
                providers = new IPasswordResetProvider[]
                {
                    _defaultPasswordResetProvider
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

        private async Task IncrementInvalidLoginAttemptCount(User user)
        {
            user.InvalidLoginAttemptCount++;
            int? maxInvalidLogins = user.LoginAttemptsBeforeLockout;
            if (maxInvalidLogins.HasValue && user.InvalidLoginAttemptCount >= maxInvalidLogins)
            {
                user.SetPermission(PermissionKind.IsDisabled, true);
                await _eventManager.PublishAsync(new UserLockedOutEventArgs(user)).ConfigureAwait(false);
                _logger.LogWarning(
                    "Disabling user {Username} due to {Attempts} unsuccessful login attempts.",
                    user.Username,
                    user.InvalidLoginAttemptCount);
            }

            await UpdateUserInternalAsync(user).ConfigureAwait(false);
        }

        private async Task UpdateUserInternalAsync(User user)
        {
            var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
            await using (dbContext.ConfigureAwait(false))
            {
                await UpdateUserInternalAsync(dbContext, user).ConfigureAwait(false);
            }
        }

        private async Task UpdateUserInternalAsync(UsersDbContext dbContext, User user)
        {
            dbContext.Users.Attach(user);
            dbContext.Entry(user).State = EntityState.Modified;
            await dbContext.SaveChangesAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes all members of this class.
        /// </summary>
        /// <param name="disposing">Defines if the class has been cleaned up by a dispose or finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _userLock.Dispose();
            }
        }
    }
}

