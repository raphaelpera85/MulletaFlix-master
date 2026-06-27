using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MulletaFlix.Database.Implementations.Contexts;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Server.Implementations.Users;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Users;

public sealed class UserManagerAuthenticationLockTests : IDisposable
{
    private readonly DbContextOptions<UsersDbContext> _dbOptions;
    private readonly Mock<IDbContextFactory<UsersDbContext>> _dbFactoryMock;
    private readonly Mock<INetworkManager> _networkManagerMock;
    private readonly Mock<IServerConfigurationManager> _configurationManagerMock;
    private readonly Mock<IServerApplicationPaths> _applicationPathsMock;
    private readonly Mock<IApplicationHost> _appHostMock;
    private readonly DefaultAuthenticationProvider _defaultAuthenticationProvider;
    private readonly InvalidAuthProvider _invalidAuthProvider;
    private readonly DefaultPasswordResetProvider _defaultPasswordResetProvider;

    public UserManagerAuthenticationLockTests()
    {
        _dbOptions = new DbContextOptionsBuilder<UsersDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using (var context = CreateDbContext())
        {
            context.Database.EnsureCreated();
        }

        _dbFactoryMock = new Mock<IDbContextFactory<UsersDbContext>>();
        _dbFactoryMock.Setup(f => f.CreateDbContext()).Returns(CreateDbContext);
        _dbFactoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDbContext);

        _networkManagerMock = new Mock<INetworkManager>();
        _networkManagerMock.Setup(m => m.IsInLocalNetwork(It.IsAny<string>())).Returns(true);

        _applicationPathsMock = new Mock<IServerApplicationPaths>();
        _applicationPathsMock.Setup(x => x.ProgramDataPath).Returns(Path.GetTempPath());

        _configurationManagerMock = new Mock<IServerConfigurationManager>();
        _configurationManagerMock.Setup(x => x.ApplicationPaths).Returns(_applicationPathsMock.Object);

        _appHostMock = new Mock<IApplicationHost>();

        var cryptoProviderMock = new Mock<ICryptoProvider>();
        _defaultAuthenticationProvider = new DefaultAuthenticationProvider(
            NullLogger<DefaultAuthenticationProvider>.Instance,
            cryptoProviderMock.Object);
        _invalidAuthProvider = new InvalidAuthProvider();
        _defaultPasswordResetProvider = new DefaultPasswordResetProvider(
            _configurationManagerMock.Object,
            _appHostMock.Object);
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task AuthenticateUser_WhenUsernameDoesNotExist_DoesNotSerializeDistinctUsernamesTogether()
    {
        var authProvider = new BlockingAuthenticationProvider();
        using var userManager = CreateUserManager(authProvider);

        var firstTask = userManager.AuthenticateUser("missing-user-one", "password", "127.0.0.1", isUserSession: true);
        var secondTask = userManager.AuthenticateUser("missing-user-two", "password", "127.0.0.1", isUserSession: true);

        await authProvider.SecondCallStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, authProvider.MaxConcurrentCalls);

        authProvider.Release();

        await Assert.ThrowsAsync<AuthenticationException>(() => firstTask);
        await Assert.ThrowsAsync<AuthenticationException>(() => secondTask);
    }

    [Fact]
    public async Task AuthenticateUser_WhenUserExists_SerializesByUserIdentity()
    {
        var authProvider = new BlockingAuthenticationProvider();
        using var userManager = CreateUserManager(authProvider);

        var user = await userManager.CreateUserAsync("existing-user");
        user.AuthenticationProviderId = authProvider.GetType().FullName!;
        await userManager.UpdateUserAsync(user);

        var firstTask = userManager.AuthenticateUser("existing-user", string.Empty, "127.0.0.1", isUserSession: true);
        var secondTask = userManager.AuthenticateUser("existing-user", string.Empty, "127.0.0.1", isUserSession: true);

        await authProvider.FirstCallStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(authProvider.SecondCallStarted.IsCompleted);
        Assert.Equal(1, authProvider.MaxConcurrentCalls);

        authProvider.Release();

        var firstResult = await firstTask;
        var secondResult = await secondTask;

        Assert.NotNull(firstResult);
        Assert.NotNull(secondResult);
        Assert.Equal(user.Id, firstResult.Id);
        Assert.Equal(user.Id, secondResult.Id);
        Assert.Equal(1, authProvider.MaxConcurrentCalls);
    }

    private UserManager CreateUserManager(IAuthenticationProvider authenticationProvider)
    {
        var userManager = new UserManager(
            _dbFactoryMock.Object,
            new NoopEventManager(),
            _networkManagerMock.Object,
            _appHostMock.Object,
            Mock.Of<IImageProcessor>(),
            NullLogger<UserManager>.Instance,
            _configurationManagerMock.Object,
            new IPasswordResetProvider[] { _defaultPasswordResetProvider },
            new IAuthenticationProvider[] { authenticationProvider, _defaultAuthenticationProvider, _invalidAuthProvider });

        SetSchemaInitialized(userManager);
        return userManager;
    }

    private UsersDbContext CreateDbContext()
    {
        return new UsersDbContext(
            _dbOptions,
            NullLogger<UsersDbContext>.Instance);
    }

    private static void SetSchemaInitialized(UserManager userManager)
    {
        var field = typeof(UserManager).GetField("_schemaInitialized", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(userManager, true);
    }

    private sealed class BlockingAuthenticationProvider : IAuthenticationProvider
    {
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _secondCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeCalls;
        private int _maxConcurrentCalls;

        public string Name => nameof(BlockingAuthenticationProvider);

        public bool IsEnabled => true;

        public Task FirstCallStarted => _firstCallStarted.Task;

        public Task SecondCallStarted => _secondCallStarted.Task;

        public int MaxConcurrentCalls => Volatile.Read(ref _maxConcurrentCalls);

        public void Release()
        {
            _release.TrySetResult(true);
        }

        public async Task<ProviderAuthenticationResult> Authenticate(string username, string password)
        {
            var currentCalls = Interlocked.Increment(ref _activeCalls);
            UpdateMaxConcurrentCalls(currentCalls);

            if (currentCalls == 1)
            {
                _firstCallStarted.TrySetResult(true);
            }
            else if (currentCalls == 2)
            {
                _secondCallStarted.TrySetResult(true);
            }

            try
            {
                await _release.Task.ConfigureAwait(false);
                return new ProviderAuthenticationResult
                {
                    Username = username
                };
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        public Task ChangePassword(MulletaFlix.Database.Implementations.Entities.User user, string newPassword)
        {
            return Task.CompletedTask;
        }

        private void UpdateMaxConcurrentCalls(int currentCalls)
        {
            while (true)
            {
                var observed = Volatile.Read(ref _maxConcurrentCalls);
                if (currentCalls <= observed)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrentCalls, currentCalls, observed) == observed)
                {
                    return;
                }
            }
        }
    }

    private sealed class NoopEventManager : IEventManager
    {
        public void Publish<T>(T eventArgs)
            where T : EventArgs
        {
        }

        public Task PublishAsync<T>(T eventArgs)
            where T : EventArgs
        {
            return Task.CompletedTask;
        }
    }
}
