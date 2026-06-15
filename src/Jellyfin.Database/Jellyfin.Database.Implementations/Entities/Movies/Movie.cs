using System;
using System.Collections.Generic;

namespace MulletaFlix.Database.Implementations.Entities.Movies;

public class Movie
{
    public int Id { get; set; }
    public Guid BaseItemId { get; set; }
    public string? Name { get; set; }
    public string? Overview { get; set; }
    public int? ProductionYear { get; set; }
    public double? Runtime { get; set; }
    public float? CommunityRating { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<MovieMetadata> Metadata { get; set; } = new HashSet<MovieMetadata>();
    public ICollection<MovieUserData> UserData { get; set; } = new HashSet<MovieUserData>();
}
