using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MulletaFlix.Api.Models.AiMetadata;

/// <summary>
/// AI metadata configuration DTO.
/// </summary>
public class AiMetadataConfigurationDto
{
    /// <summary>
    /// Gets or sets a value indicating whether AI metadata assistance is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the provider decision mode.
    /// </summary>
    public string DecisionMode { get; set; } = "single";

    /// <summary>
    /// Gets or sets the primary provider identifier.
    /// </summary>
    public string? PrimaryProviderId { get; set; }

    /// <summary>
    /// Gets or sets the judge provider identifier.
    /// </summary>
    public string? JudgeProviderId { get; set; }

    /// <summary>
    /// Gets or sets the configured providers.
    /// </summary>
    public IReadOnlyList<AiMetadataProviderDto> Providers { get; set; } = [];

    /// <summary>
    /// Gets or sets the automation rules.
    /// </summary>
    public AiMetadataAutomationDto Automation { get; set; } = new();

    /// <summary>
    /// Gets or sets the media type switches.
    /// </summary>
    public AiMetadataMediaTypesDto MediaTypes { get; set; } = new();
}

/// <summary>
/// AI metadata provider DTO.
/// </summary>
public class AiMetadataProviderDto
{
    /// <summary>
    /// Gets or sets the stable local provider identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider type.
    /// </summary>
    [Required]
    public string Provider { get; set; } = "OpenAI";

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this provider is active.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the API base URL.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model name.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a new API key value. Empty values preserve the saved key.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the saved key should be removed.
    /// </summary>
    public bool ClearApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a key is saved.
    /// </summary>
    public bool ApiKeyConfigured { get; set; }
}

/// <summary>
/// AI metadata automation DTO.
/// </summary>
public class AiMetadataAutomationDto
{
    /// <summary>
    /// Gets or sets the minimum confidence for a suggestion to appear.
    /// </summary>
    public int MinimumSuggestionConfidence { get; set; } = 70;

    /// <summary>
    /// Gets or sets the minimum confidence for automatic apply.
    /// </summary>
    public int AutomaticApplyConfidence { get; set; } = 90;

    /// <summary>
    /// Gets or sets a value indicating whether high-confidence suggestions can be applied automatically.
    /// </summary>
    public bool AllowAutomaticApply { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether at least two providers must agree before automatic apply.
    /// </summary>
    public bool RequireTwoProviderAgreement { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether existing EPG values should be protected.
    /// </summary>
    public bool ProtectExistingEpg { get; set; } = true;

    /// <summary>
    /// Gets or sets the confidence required to replace an existing EPG value.
    /// </summary>
    public int ExistingEpgReplaceConfidence { get; set; } = 95;

    /// <summary>
    /// Gets or sets a value indicating whether manually edited metadata should be preserved.
    /// </summary>
    public bool ProtectManualMetadata { get; set; } = true;
}

/// <summary>
/// AI metadata media type switches DTO.
/// </summary>
public class AiMetadataMediaTypesDto
{
    /// <summary>
    /// Gets or sets a value indicating whether movie metadata can be processed.
    /// </summary>
    public bool Movies { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether series metadata can be processed.
    /// </summary>
    public bool Series { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether book metadata can be processed.
    /// </summary>
    public bool Books { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Live TV channel metadata can be processed.
    /// </summary>
    public bool Channels { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether logos can be suggested.
    /// </summary>
    public bool Logos { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether EPG mappings can be suggested.
    /// </summary>
    public bool Epg { get; set; } = true;
}

/// <summary>
/// Provider connection test request.
/// </summary>
public class AiMetadataProviderTestRequest
{
    /// <summary>
    /// Gets or sets the provider identifier to test from saved configuration.
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// Gets or sets inline provider settings for testing before save.
    /// </summary>
    public AiMetadataProviderDto? Provider { get; set; }
}

/// <summary>
/// Provider connection test result.
/// </summary>
public class AiMetadataProviderTestResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the provider configuration is usable.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the result message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// AI metadata run request.
/// </summary>
public class AiMetadataRunRequest
{
    /// <summary>
    /// Gets or sets the run scope.
    /// </summary>
    public string Scope { get; set; } = "configured";
}

/// <summary>
/// Normalization result returned by an AI provider.
/// </summary>
public class AiMetadataNormalizationDto
{
    /// <summary>
    /// Gets or sets the normalized title.
    /// </summary>
    public string? NormalizedTitle { get; set; }

    /// <summary>
    /// Gets or sets the original title.
    /// </summary>
    public string? OriginalTitle { get; set; }

    /// <summary>
    /// Gets or sets the year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the confidence score.
    /// </summary>
    public int Confidence { get; set; }

    /// <summary>
    /// Gets or sets the explanation.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item should be skipped.
    /// </summary>
    public bool Skip { get; set; }
}

/// <summary>
/// AI metadata activity status.
/// </summary>
public class AiMetadataActivityItemDto
{
    /// <summary>
    /// Gets or sets the activity identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the activity creation date.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the activity update date.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the current status.
    /// </summary>
    public string Status { get; set; } = "Queued";

    /// <summary>
    /// Gets or sets a short title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current step.
    /// </summary>
    public string CurrentStep { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current phase inside the item analysis.
    /// </summary>
    public string CurrentPhase { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the selected providers.
    /// </summary>
    public IReadOnlyList<string> Providers { get; set; } = [];

    /// <summary>
    /// Gets or sets the selected media types.
    /// </summary>
    public IReadOnlyList<string> MediaTypes { get; set; } = [];

    /// <summary>
    /// Gets or sets the progress percentage.
    /// </summary>
    public int Progress { get; set; }

    /// <summary>
    /// Gets or sets the progress percentage for the current phase.
    /// </summary>
    public int PhaseProgress { get; set; }

    /// <summary>
    /// Gets or sets the result summary.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the activity log lines.
    /// </summary>
    public IReadOnlyList<string> Logs { get; set; } = [];
}
