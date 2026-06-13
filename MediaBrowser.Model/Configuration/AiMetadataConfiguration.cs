#pragma warning disable CA1819

namespace MediaBrowser.Model.Configuration;

/// <summary>
/// AI-assisted metadata configuration.
/// </summary>
public class AiMetadataConfiguration
{
    /// <summary>
    /// The named configuration key.
    /// </summary>
    public const string ConfigurationKey = "ai-metadata";

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
    /// Gets or sets the judge provider identifier for council mode.
    /// </summary>
    public string? JudgeProviderId { get; set; }

    /// <summary>
    /// Gets or sets the configured providers.
    /// </summary>
    public AiMetadataProviderConfiguration[] Providers { get; set; } = [];

    /// <summary>
    /// Gets or sets the automation rules.
    /// </summary>
    public AiMetadataAutomationConfiguration Automation { get; set; } = new();

    /// <summary>
    /// Gets or sets the media type switches.
    /// </summary>
    public AiMetadataMediaTypesConfiguration MediaTypes { get; set; } = new();
}

/// <summary>
/// AI metadata provider configuration.
/// </summary>
public class AiMetadataProviderConfiguration
{
    /// <summary>
    /// Gets or sets the stable local provider identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider type.
    /// </summary>
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
    /// Gets or sets the protected API key payload.
    /// </summary>
    public string ProtectedApiKey { get; set; } = string.Empty;
}

/// <summary>
/// AI metadata automation thresholds.
/// </summary>
public class AiMetadataAutomationConfiguration
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
/// AI metadata media type switches.
/// </summary>
public class AiMetadataMediaTypesConfiguration
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
