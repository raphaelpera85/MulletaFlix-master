using System;

namespace MulletaFlix.Database.Implementations.Entities.Series;

public class SeriesUserData
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public int SeriesId { get; set; }
    public bool Played { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime? LastPlayedDate { get; set; }
    public Series Series { get; set; } = null!;
}
