using System;

namespace MulletaFlix.Database.Implementations.Entities.Series;

public class Episode
{
    public int Id { get; set; }
    public int SeasonId { get; set; }
    public Guid BaseItemId { get; set; }
    public string? Name { get; set; }
    public int? IndexNumber { get; set; }
    public int? ParentIndexNumber { get; set; }
    public long? RunTimeTicks { get; set; }
    public bool IsActive { get; set; } = true;
    public Season Season { get; set; } = null!;
}
