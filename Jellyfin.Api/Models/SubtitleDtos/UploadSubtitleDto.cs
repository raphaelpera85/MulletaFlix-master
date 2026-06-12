using System.ComponentModel.DataAnnotations;

namespace MulletaFlix.Api.Models.SubtitleDtos;

/// <summary>
/// Upload subtitles dto.
/// </summary>
public class UploadSubtitleDto
{
    /// <summary>
    /// Gets or sets the subtitle language.
    /// </summary>
    [Required]
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the subtitle format.
    /// </summary>
    [Required]
    [RegularExpression("^(srt|ass|ssa|vtt|sub|idx|smi)$", ErrorMessage = "Formato de legenda não suportado.")]
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the subtitle is forced.
    /// </summary>
    [Required]
    public bool IsForced { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the subtitle is for hearing impaired.
    /// </summary>
    [Required]
    public bool IsHearingImpaired { get; set; }

    /// <summary>
    /// Gets or sets the subtitle data (base64 encoded, max 5MB).
    /// </summary>
    [Required]
    [StringLength(7_000_000, MinimumLength = 1, ErrorMessage = "Dados da legenda muito grandes.")]
    public string Data { get; set; } = string.Empty;
}

