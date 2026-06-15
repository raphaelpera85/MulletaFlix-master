using System;
using System.Collections.Generic;

namespace MulletaFlix.Database.Implementations.Entities.Channels;

public class Channel
{
    public int Id { get; set; }
    public Guid BaseItemId { get; set; }
    public string? Name { get; set; }
    public string? ChannelNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Program> Programs { get; set; } = new HashSet<Program>();
}
