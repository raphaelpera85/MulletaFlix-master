using System;
using System.Collections.Generic;

namespace MulletaFlix.Database.Implementations.Entities.Series;

public class Series
{
    public int Id { get; set; }
    public Guid BaseItemId { get; set; }
    public string? Name { get; set; }
    public string? Overview { get; set; }
    public int? ProductionYear { get; set; }
    public string? Status { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Season> Seasons { get; set; } = new HashSet<Season>();
    public ICollection<SeriesUserData> UserData { get; set; } = new HashSet<SeriesUserData>();
}
