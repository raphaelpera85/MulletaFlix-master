using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MulletaFlix.Database.Implementations.Entities;

public class MidiaStorageOnlineMediaMetadata
{
    [Key]
    [MaxLength(512)]
    public string RelativePath { get; set; } = string.Empty;

    [MaxLength(16)]
    public string ContentType { get; set; } = string.Empty;

    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    [MaxLength(16)]
    public string Mode { get; set; } = "strm";

    [MaxLength(2048)]
    public string SourceUrl { get; set; } = string.Empty;

    [MaxLength(255)]
    public string SourceId { get; set; } = string.Empty;

    [MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    public DateTime RecognizedAtUtc { get; set; } = DateTime.UtcNow;
}
