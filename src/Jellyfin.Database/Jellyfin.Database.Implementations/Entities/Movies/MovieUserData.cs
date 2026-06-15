using System;

namespace MulletaFlix.Database.Implementations.Entities.Movies;

public class MovieUserData
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public int MovieId { get; set; }
    public bool Played { get; set; }
    public int PlayCount { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime? LastPlayedDate { get; set; }
    public Movie Movie { get; set; } = null!;
}
