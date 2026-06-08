using System;
using System.ComponentModel.DataAnnotations;

namespace MulletaFlix.Database.Implementations.Entities;

/// <summary>
/// An entity representing a user's license/subscription.
/// Controls time-limited access to the MulletaFlix server.
/// </summary>
public class UserLicense
{
    /// <summary>
    /// Gets or sets the primary key.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the user's Id (FK).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the associated user.
    /// </summary>
    public virtual User User { get; set; } = null!;

    /// <summary>
    /// Gets or sets the date when the license was activated.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Gets or sets the duration of the license in hours.
    /// Null means the license is unlimited.
    /// </summary>
    public int? DurationHours { get; set; }

    /// <summary>
    /// Gets or sets the calculated expiration date.
    /// Null means the license never expires.
    /// </summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the license is unlimited (never expires).
    /// </summary>
    public bool IsUnlimited { get; set; }

    /// <summary>
    /// Gets or sets optional admin notes about this license.
    /// </summary>
    [MaxLength(1024)]
    [StringLength(1024)]
    public string? AdminNotes { get; set; }

    /// <summary>
    /// Gets or sets the Id of the admin user who granted this license.
    /// </summary>
    public Guid? GrantedByUserId { get; set; }

    /// <summary>
    /// Gets or sets the date when this record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date when this record was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

