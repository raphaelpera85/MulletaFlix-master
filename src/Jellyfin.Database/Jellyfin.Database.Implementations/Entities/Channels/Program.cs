using System;

namespace MulletaFlix.Database.Implementations.Entities.Channels;

public class Program
{
    public int Id { get; set; }
    public int ChannelId { get; set; }
    public Guid BaseItemId { get; set; }
    public string? Name { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public Channel Channel { get; set; } = null!;
}
