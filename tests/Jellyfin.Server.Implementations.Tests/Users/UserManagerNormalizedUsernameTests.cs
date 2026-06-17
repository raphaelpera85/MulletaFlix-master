using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.Contexts;
using MulletaFlix.Database.Implementations.Locking;
using MulletaFlix.Database.Providers.Sqlite;
using MulletaFlix.Server.Implementations.Users;
using MediaBrowser.Common;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Users
{
    public sealed class UserManagerNormalizedUsernameTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<UsersDbContext> _dbOptions;
        private readonly UserManager _userManager;

        public UserManagerNormalizedUsernameTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            _dbOptions = new DbContextOptionsBuilder<UsersDbContext>()
                .UseSqlite(_connection)
                .Options;

            // Create the schema
            using var ctx = CreateDbContext();
            ctx.Database.EnsureCreated();

            var factory = new Mock<IDbContextFactory<UsersDbContext>>();
            factory.Setup(f => f.CreateDbContext()).Returns(CreateDbContext);
            factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateDbContext);

            var cryptoProvider = new Mock<ICryptoProvider>();
            var configManager = new Mock<IServerConfigurationManager>();
            var appPaths = new Mock<IServerApplicationPaths>();
            appPaths.Setup(x => x.ProgramDataPath).Returns(Path.GetTempPath());
            configManager.Setup(x => x.ApplicationPaths).Returns(appPaths.Object);

            var appHost = new Mock<IApplicationHost>();

            var defaultAuthProvider = new DefaultAuthenticationProvider(
                NullLogger<DefaultAuthenticationProvider>.Instance,
                cryptoProvider.Object);
            var invalidAuthProvider = new InvalidAuthProvider();
            var defaultPasswordResetProvider = new DefaultPasswordResetProvider(
                configManager.Object,
                appHost.Object);

            _userManager = new UserManager(
                factory.Object,
                new NoopEventManager(),
                new Mock<INetworkManager>().Object,
                appHost.Object,
                new Mock<IImageProcessor>().Object,
                NullLogger<UserManager>.Instance,
                configManager.Object,
                new IPasswordResetProvider[] { defaultPasswordResetProvider },
                new IAuthenticationProvider[] { defaultAuthProvider, invalidAuthProvider });
        }

        public void Dispose()
        {
            _userManager.Dispose();
            _connection.Dispose();
        }

        private UsersDbContext CreateDbContext()
        {
            return new UsersDbContext(
                _dbOptions,
                NullLogger<UsersDbContext>.Instance);
        }

        // ----- GetUserByName tests -----

        [Theory]
        // German umlauts
        [InlineData("m\u00FCnchen", "M\u00DCNCHEN")]
        // Spanish tilde-n
        [InlineData("\u00D1o\u00F1o", "\u00D1O\u00D1O")]
        // ASCII, invariant uppercase lookup
        [InlineData("MulletaFlix", "MulletaFlix")]
        // Turkish cedilla: invariant 'i' uppercases to 'I' (U+0049), not Turkish '\u0130' (U+0130)
        [InlineData("\u00C7elebi", "\u00C7ELEBI")]
        public async Task GetUserByName_WithNonAsciiUsername_FindsUserByNormalizedName(
            string username, string normalizedLookup)
        {
            await _userManager.CreateUserAsync(username);

            var found = _userManager.GetUserByName(normalizedLookup);

            Assert.NotNull(found);
            Assert.Equal(username, found.Username);
        }

        [Theory]
        // German umlaut, look up by both upper and lower case
        [InlineData("m\u00FCnchen")]
        // Spanish tilde-n
        [InlineData("\u00D1o\u00F1o")]
        // lowercase 'i' \u2014 invariant ToUpperInvariant gives 'I', not Turkish '\u0130'
        [InlineData("ali")]
        // mixed ASCII + umlaut
        [InlineData("test\u00FCser")]
        public async Task GetUserByName_WithVariousCase_FindsUserCaseInsensitively(string username)
        {
            await _userManager.CreateUserAsync(username);

            var upperFound = _userManager.GetUserByName(username.ToUpperInvariant());
            var lowerFound = _userManager.GetUserByName(username.ToLowerInvariant());
            var exactFound = _userManager.GetUserByName(username);

            Assert.NotNull(upperFound);
            Assert.NotNull(lowerFound);
            Assert.NotNull(exactFound);
        }

        [Theory]
        [InlineData("nonexistent")]
        // No user with NormalizedUsername = "M\u00DCNCHEN" has been created
        [InlineData("M\u00DCNCHEN")]
        public void GetUserByName_WhenUserDoesNotExist_ReturnsNull(string lookupName)
        {
            var result = _userManager.GetUserByName(lookupName);

            Assert.Null(result);
        }

        // ----- CreateUserAsync duplicate detection tests -----

        [Theory]
        // German umlaut, case-swapped duplicate
        [InlineData("m\u00FCnchen", "M\u00DCNCHEN")]
        // Spanish tilde-n, lowercase duplicate
        [InlineData("\u00D1o\u00F1o", "\u00F1o\u00F1o")]
        // ASCII, uppercase duplicate
        [InlineData("alice", "ALICE")]
        // Turkish cedilla: "\u00E7elebi".ToUpperInvariant() == "\u00C7ELEBI" == "\u00C7ELEBI".ToUpperInvariant()
        [InlineData("\u00E7elebi", "\u00C7ELEBI")]
        public async Task CreateUserAsync_WhenNormalizedNameAlreadyExists_ThrowsArgumentException(
            string existingUsername, string duplicateUsername)
        {
            await _userManager.CreateUserAsync(existingUsername);

            await Assert.ThrowsAsync<ArgumentException>(
                () => _userManager.CreateUserAsync(duplicateUsername));
        }

        [Theory]
        // Different non-ASCII names that do not collide after normalization
        [InlineData("m\u00FCnchen", "m\u00FCnchen2")]
        [InlineData("ali", "ali2")]
        // Visually similar but different Unicode code points: \u00F1 (U+00F1) vs n (U+006E)
        [InlineData("no\u00F1o", "nono")]
        public async Task CreateUserAsync_WithDistinctNonAsciiUsernames_CreatesBothUsers(
            string firstUsername, string secondUsername)
        {
            var first = await _userManager.CreateUserAsync(firstUsername);
            var second = await _userManager.CreateUserAsync(secondUsername);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotEqual(first.Id, second.Id);
        }

        // ----- RenameUser tests -----

        [Theory]
        // Rename to non-ASCII name
        [InlineData("alice", "m\u00FCnchen")]
        // Rename between similar non-ASCII and ASCII
        [InlineData("m\u00FCller", "mueller")]
        // Contains 'i': invariant uppercase is always 'I', never Turkish '\u0130'
        [InlineData("ali", "ALI2")]
        // Rename to Spanish tilde-n name
        [InlineData("testuser", "\u00D1o\u00F1o")]
        public async Task RenameUser_SetsNormalizedUsernameToUpperInvariant(
            string originalName, string newName)
        {
            var user = await _userManager.CreateUserAsync(originalName);

            await _userManager.RenameUser(user.Id, originalName, newName);

            var renamed = _userManager.GetUserById(user.Id);
            Assert.NotNull(renamed);
            Assert.Equal(newName, renamed.Username);
            Assert.Equal(newName.ToUpperInvariant(), renamed.NormalizedUsername);
        }

        [Theory]
        // Same name different case: NormalizedUsername already taken
        [InlineData("m\u00FCnchen", "M\u00DCNCHEN")]
        // Spanish, lowercase conflicts with existing uppercase-normalised entry
        [InlineData("\u00D1o\u00F1o", "\u00F1o\u00F1o")]
        // ASCII, capitalised conflict
        [InlineData("alice", "Alice")]
        // Mixed ASCII + umlaut
        [InlineData("test\u00FCser", "TEST\u00DCSER")]
        public async Task RenameUser_WhenNormalizedNameConflictsWithExistingUser_ThrowsArgumentException(
            string existingUsername, string conflictingNewName)
        {
            var targetUser = await _userManager.CreateUserAsync("renametarget");
            await _userManager.CreateUserAsync(existingUsername);

            await Assert.ThrowsAsync<ArgumentException>(
                () => _userManager.RenameUser(targetUser.Id, "renametarget", conflictingNewName));
        }

        [Fact]
        public async Task InitializeAsync_WhenUsersTableIsMissing_CreatesTheSchemaAndFirstUser()
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var dbOptions = new DbContextOptionsBuilder<UsersDbContext>()
                .UseSqlite(connection)
                .Options;

            var factory = new Mock<IDbContextFactory<UsersDbContext>>();
            factory.Setup(f => f.CreateDbContext()).Returns(() => new UsersDbContext(dbOptions, NullLogger<UsersDbContext>.Instance));
            factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new UsersDbContext(dbOptions, NullLogger<UsersDbContext>.Instance));

            var cryptoProvider = new Mock<ICryptoProvider>();
            var configManager = new Mock<IServerConfigurationManager>();
            var appPaths = new Mock<IServerApplicationPaths>();
            appPaths.Setup(x => x.ProgramDataPath).Returns(Path.GetTempPath());
            configManager.Setup(x => x.ApplicationPaths).Returns(appPaths.Object);

            var appHost = new Mock<IApplicationHost>();
            var defaultAuthProvider = new DefaultAuthenticationProvider(
                NullLogger<DefaultAuthenticationProvider>.Instance,
                cryptoProvider.Object);
            var invalidAuthProvider = new InvalidAuthProvider();
            var defaultPasswordResetProvider = new DefaultPasswordResetProvider(
                configManager.Object,
                appHost.Object);

            var userManager = new UserManager(
                factory.Object,
                new NoopEventManager(),
                new Mock<INetworkManager>().Object,
                appHost.Object,
                new Mock<IImageProcessor>().Object,
                NullLogger<UserManager>.Instance,
                configManager.Object,
                new IPasswordResetProvider[] { defaultPasswordResetProvider },
                new IAuthenticationProvider[] { defaultAuthProvider, invalidAuthProvider });

            await userManager.InitializeAsync();

            var users = userManager.GetUsers();
            var firstUser = userManager.GetFirstUser();

            Assert.Single(users);
            Assert.NotNull(firstUser);
            Assert.False(string.IsNullOrWhiteSpace(firstUser!.Username));

            userManager.Dispose();
        }

        private sealed class NoopEventManager : IEventManager
        {
            public void Publish<T>(T eventArgs)
                where T : EventArgs
            {
            }

            public Task PublishAsync<T>(T eventArgs)
                where T : EventArgs
                => Task.CompletedTask;
        }
    }
}

