using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Data;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Server.Implementations.Users;

/// <summary>
/// Manages user licenses/subscriptions.
/// </summary>
public class UserLicenseManager : IUserLicenseManager
{
    private readonly IDbContextFactory<MulletaFlixDbContext> _dbProvider;
    private readonly IUserManager _userManager;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<UserLicenseManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserLicenseManager"/> class.
    /// </summary>
    /// <param name="dbProvider">The database provider.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="logger">The logger.</param>
    public UserLicenseManager(
        IDbContextFactory<MulletaFlixDbContext> dbProvider,
        IUserManager userManager,
        ISessionManager sessionManager,
        ILogger<UserLicenseManager> logger)
    {
        _dbProvider = dbProvider;
        _userManager = userManager;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<UserLicenseDto?> GetLicenseAsync(Guid userId)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var license = await dbContext.UserLicenses
                .AsNoTracking()
                .Include(l => l.User)
                .FirstOrDefaultAsync(l => l.UserId.Equals(userId))
                .ConfigureAwait(false);

            if (license is null)
            {
                return null;
            }

            return MapToDto(license);
        }
    }

    /// <inheritdoc/>
    public async Task<UserLicenseDto> SetLicenseAsync(Guid userId, int? durationHours, string? adminNotes, Guid grantedByUserId)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var user = await dbContext.Users
                .Include(u => u.Permissions)
                .FirstOrDefaultAsync(u => u.Id.Equals(userId))
                .ConfigureAwait(false);

            if (user is null)
            {
                throw new InvalidOperationException($"User {userId} not found.");
            }

            var license = await dbContext.UserLicenses
                .FirstOrDefaultAsync(l => l.UserId.Equals(userId))
                .ConfigureAwait(false);

            var now = DateTime.UtcNow;
            bool isUnlimited = !durationHours.HasValue
                || durationHours.Value == -1
                || user.HasPermission(PermissionKind.IsAdministrator);

            if (license is null)
            {
                // Create new license
                license = new UserLicense
                {
                    UserId = userId,
                    StartDate = now,
                    DurationHours = isUnlimited ? null : durationHours,
                    ExpirationDate = isUnlimited ? null : CalculateExpiration(now, durationHours!.Value),
                    IsUnlimited = isUnlimited,
                    AdminNotes = adminNotes,
                    GrantedByUserId = grantedByUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                dbContext.UserLicenses.Add(license);
            }
            else
            {
                // Renew/update existing license
                // If the current license is still active, extend from the current expiration.
                // If it is expired, start the new period from now.
                var renewalStartDate = GetRenewalStartDate(license, now);

                license.StartDate = isUnlimited ? now : renewalStartDate;
                license.DurationHours = isUnlimited ? null : durationHours;
                license.ExpirationDate = isUnlimited ? null : CalculateExpiration(license.StartDate, durationHours!.Value);
                license.IsUnlimited = isUnlimited;
                license.AdminNotes = adminNotes;
                license.GrantedByUserId = grantedByUserId;
                license.UpdatedAt = now;
            }

            await dbContext.SaveChangesAsync().ConfigureAwait(false);

            // Reload with navigation for DTO mapping
            license = await dbContext.UserLicenses
                .AsNoTracking()
                .Include(l => l.User)
                .FirstAsync(l => l.UserId.Equals(userId))
                .ConfigureAwait(false);

            return MapToDto(license);
        }
    }

    /// <inheritdoc/>
    public async Task RevokeLicenseAsync(Guid userId)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var license = await dbContext.UserLicenses
                .FirstOrDefaultAsync(l => l.UserId.Equals(userId))
                .ConfigureAwait(false);

            if (license is not null)
            {
                dbContext.UserLicenses.Remove(license);
                await dbContext.SaveChangesAsync().ConfigureAwait(false);
                _logger.LogInformation("License revoked for user {UserId}.", userId);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<int> ExpireOutdatedLicensesAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var disabledCount = 0;

        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            // Find all expired, non-unlimited licenses whose users are still enabled
            var expiredLicenses = await dbContext.UserLicenses
                .Include(l => l.User)
                    .ThenInclude(u => u.Permissions)
                .Where(l => !l.IsUnlimited
                    && l.ExpirationDate != null
                    && l.ExpirationDate < now)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var license in expiredLicenses)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (license.User is null)
                {
                    continue;
                }

                // Skip users already disabled
                // Skip admin users â€” admins cannot be disabled by license expiration
                if (license.User.HasPermission(PermissionKind.IsAdministrator))
                {
                    _logger.LogWarning(
                        "Skipping license expiration for admin user {UserName} (Id: {UserId}).",
                        license.User.Username,
                        license.User.Id);
                    continue;
                }

                _logger.LogInformation(
                    "User {UserName} (Id: {UserId}) has an expired license at {ExpirationDate}, but playback is not blocked by license expiration.",
                    license.User.Username,
                    license.User.Id,
                    license.ExpirationDate);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return disabledCount;
    }

    /// <inheritdoc/>
    public async Task<bool> IsLicenseExpiredAsync(Guid userId)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var license = await dbContext.UserLicenses
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.UserId.Equals(userId))
                .ConfigureAwait(false);

            if (license is null)
            {
                // No license = no restriction (backwards compatible)
                return false;
            }

            if (license.IsUnlimited)
            {
                return false;
            }

            return license.ExpirationDate.HasValue && license.ExpirationDate.Value < DateTime.UtcNow;
        }
    }

    private static DateTime CalculateExpiration(DateTime startDate, int durationHours)
    {
        // Use calendar months for standard durations for accuracy
        return durationHours switch
        {
            0 => startDate.AddMinutes(30), // Trial: exactly 30 minutes
            1 => startDate.AddHours(1), // Trial: exactly 1 hour
            730 => startDate.AddMonths(1), // 1 month
            2190 => startDate.AddMonths(3), // 3 months
            4380 => startDate.AddMonths(6), // 6 months
            8760 => startDate.AddMonths(12), // 12 months
            _ => startDate.AddHours(durationHours) // Custom: exact hours
        };
    }

    private static DateTime GetRenewalStartDate(UserLicense license, DateTime now)
    {
        if (!license.IsUnlimited && license.ExpirationDate.HasValue && license.ExpirationDate.Value > now)
        {
            return license.ExpirationDate.Value;
        }

        return now;
    }

    private static UserLicenseDto MapToDto(UserLicense license)
    {
        var now = DateTime.UtcNow;
        var effectiveExpirationDate = GetEffectiveExpirationDate(license);
        var isExpired = !license.IsUnlimited
            && effectiveExpirationDate.HasValue
            && effectiveExpirationDate.Value < now;

        string? timeRemaining = null;
        if (license.IsUnlimited)
        {
            timeRemaining = "Ilimitada";
        }
        else if (isExpired)
        {
            timeRemaining = "Expirada";
        }
        else if (effectiveExpirationDate.HasValue)
        {
            var remaining = effectiveExpirationDate.Value - now;
            timeRemaining = remaining.TotalDays >= 1
                ? $"{(int)remaining.TotalDays} dias"
                : remaining.TotalHours >= 1
                    ? $"{(int)remaining.TotalHours}h {remaining.Minutes}min"
                    : $"{(int)remaining.TotalMinutes} min";
        }

        return new UserLicenseDto
        {
            UserId = license.UserId,
            UserName = license.User?.Username ?? string.Empty,
            StartDate = license.StartDate,
            DurationHours = license.DurationHours,
            ExpirationDate = effectiveExpirationDate,
            IsUnlimited = license.IsUnlimited,
            IsExpired = isExpired,
            TimeRemaining = timeRemaining,
            AdminNotes = license.AdminNotes,
            GrantedByUserName = null, // Would require a second user lookup; kept simple
            CreatedAt = license.CreatedAt,
            UpdatedAt = license.UpdatedAt
        };
    }

    private static DateTime? GetEffectiveExpirationDate(UserLicense license)
    {
        if (license.IsUnlimited)
        {
            return null;
        }

        if (license.ExpirationDate.HasValue)
        {
            return license.ExpirationDate;
        }

        if (license.DurationHours.HasValue)
        {
            return CalculateExpiration(license.StartDate, license.DurationHours.Value);
        }

        return null;
    }
}

