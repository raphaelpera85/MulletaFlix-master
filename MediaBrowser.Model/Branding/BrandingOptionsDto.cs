namespace MediaBrowser.Model.Branding;

/// <summary>
/// The branding options DTO for API use.
/// This DTO excludes SplashscreenLocation to prevent it from being updated via API.
/// </summary>
public class BrandingOptionsDto
{
    /// <summary>
    /// Gets or sets the login disclaimer.
    /// </summary>
    /// <value>The login disclaimer.</value>
    public string? LoginDisclaimer { get; set; }

    /// <summary>
    /// Gets or sets the custom CSS.
    /// </summary>
    /// <value>The custom CSS.</value>
    public string? CustomCss { get; set; }

    /// <summary>
    /// Gets or sets the default theme for the server.
    /// </summary>
    public string? DefaultTheme { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable the splashscreen.
    /// </summary>
    public bool SplashscreenEnabled { get; set; } = false;

    public bool IntroEnabled { get; set; }

    public string? IntroPath { get; set; }

    public bool PrebufferEnabled { get; set; }

    public int PrebufferSizeMb { get; set; } = 32;

    public bool AdSenseEnabled { get; set; }

    public string? AdSenseClientId { get; set; }

    public string? AdSenseSlotId { get; set; }

    public int AdSenseHoldSeconds { get; set; } = 8;

    public bool AdSenseShowOnLogin { get; set; }

    public bool AdSenseShowOnHome { get; set; }

    public bool AdSenseShowAfterIntro { get; set; } = true;
}
