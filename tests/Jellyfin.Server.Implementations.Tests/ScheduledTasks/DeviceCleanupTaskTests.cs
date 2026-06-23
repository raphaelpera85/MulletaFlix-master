using System;
using Emby.Server.Implementations.ScheduledTasks;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.ScheduledTasks;

public class DeviceCleanupTaskTests
{
    [Fact]
    public void ShouldDeleteDevice_RecentOfflineDevice_ReturnsFalse()
    {
        var lastActivity = DateTime.UtcNow.AddDays(-1);

        Assert.False(DeviceCleanupTask.ShouldDeleteDevice(lastActivity, false, DateTime.UtcNow.AddDays(-90)));
    }

    [Fact]
    public void ShouldDeleteDevice_ExpiredOfflineDevice_ReturnsTrue()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);

        Assert.True(DeviceCleanupTask.ShouldDeleteDevice(cutoff.AddDays(-1), false, cutoff));
    }

    [Fact]
    public void ShouldDeleteDevice_ActiveExpiredDevice_ReturnsFalse()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);

        Assert.False(DeviceCleanupTask.ShouldDeleteDevice(cutoff.AddDays(-1), true, cutoff));
    }
}
