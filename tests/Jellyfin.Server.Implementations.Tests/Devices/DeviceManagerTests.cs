using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Devices;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Database.Implementations.Entities.Security;
using MulletaFlix.Database.Implementations.Locking;
using MulletaFlix.Server.Implementations.Devices;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Devices;

public sealed class DeviceManagerTests : IDisposable
{
    private readonly DbContextOptions<MulletaFlixDbContext> _options;
    private readonly IMulletaFlixDatabaseProvider _dbProvider;
    private readonly IEntityFrameworkCoreLockingBehavior _lockingBehavior;
    private readonly MulletaFlixDbContext _context;
    private readonly DeviceManager _deviceManager;

    public DeviceManagerTests()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        _options = new DbContextOptionsBuilder<MulletaFlixDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"), databaseRoot)
            .Options;

        var dbProvider = new Mock<IMulletaFlixDatabaseProvider>();
        dbProvider.Setup(p => p.OnModelCreating(It.IsAny<ModelBuilder>()));
        _dbProvider = dbProvider.Object;

        var lockingBehavior = new Mock<IEntityFrameworkCoreLockingBehavior>();
        lockingBehavior.Setup(l => l.OnSaveChanges(It.IsAny<MulletaFlixDbContext>(), It.IsAny<Action>()))
            .Callback<MulletaFlixDbContext, Action>(static (_, action) => action());
        lockingBehavior.Setup(l => l.OnSaveChangesAsync(It.IsAny<MulletaFlixDbContext>(), It.IsAny<Func<Task>>()))
            .Callback<MulletaFlixDbContext, Func<Task>>(static (_, func) => func());
        _lockingBehavior = lockingBehavior.Object;

        _context = new MulletaFlixDbContext(
            _options,
            NullLogger<MulletaFlixDbContext>.Instance,
            _dbProvider,
            _lockingBehavior);

        _context.Database.EnsureCreated();

        _context.Devices.Add(new Device(
            Guid.NewGuid(),
            "MulletaFlix Web",
            "12.0.0",
            "Chrome",
            "device-1"));
        _context.SaveChanges();

        var dbFactory = new Mock<IDbContextFactory<MulletaFlixDbContext>>();
        dbFactory.Setup(f => f.CreateDbContext()).Returns(CreateContext);
        dbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateContext);

        var userManager = new Mock<IUserManager>();
        userManager.Setup(m => m.GetUserById(It.IsAny<Guid>()))
            .Returns((User?)null);

        _deviceManager = new DeviceManager(
            dbFactory.Object,
            userManager.Object,
            NullLogger<DeviceManager>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private MulletaFlixDbContext CreateContext()
    {
        return new MulletaFlixDbContext(
            _options,
            NullLogger<MulletaFlixDbContext>.Instance,
            _dbProvider,
            _lockingBehavior);
    }

    [Fact]
    public void GetDevicesForUser_IgnoresMissingLastUserAndStillReturnsDevices()
    {
        var result = _deviceManager.GetDevicesForUser(null);

        Assert.Equal(1, result.TotalRecordCount);
        Assert.Single(result.Items);
        Assert.Null(result.Items[0].LastUserName);
        Assert.Equal("Chrome", result.Items[0].Name);
    }
}
