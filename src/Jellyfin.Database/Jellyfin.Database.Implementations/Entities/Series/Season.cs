using System;
using System.Collections.Generic;

namespace MulletaFlix.Database.Implementations.Entities.Series;

public class Season
{
    public int Id { get; set; }
    public int SeriesId { get; set; }
    public Guid BaseItemId { get; set; }
    public string? Name { get; set; }
    public int? IndexNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public Series Series { get; set; } = null!;
    public ICollection<Episode> Episodes { get; set; } = new HashSet<Episode>();
}
