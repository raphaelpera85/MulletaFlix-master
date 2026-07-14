namespace MediaBrowser.Model.Branding;

/// <summary>
/// The branding options.
/// </summary>
public class BrandingOptions
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

    /// <summary>
    /// Gets or sets the splashscreen location on disk.
    /// </summary>
    public string? SplashscreenLocation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether native local intros are enabled.
    /// </summary>
    public bool IntroEnabled { get; set; }

    /// <summary>
    /// Gets or sets the local intro video path.
    /// </summary>
    public string? IntroPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether STRM prebuffering is enabled.
    /// </summary>
    public bool PrebufferEnabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum prebuffer size in megabytes.
    /// </summary>
    public int PrebufferSizeMb { get; set; } = 32;

    /// <summary>
    /// Gets or sets a value indicating whether AdSense interstitials are enabled.
    /// </summary>
    public bool AdSenseEnabled { get; set; }

    /// <summary>
    /// Gets or sets the AdSense client id.
    /// </summary>
    public string? AdSenseClientId { get; set; }

    /// <summary>
    /// Gets or sets the AdSense slot id.
    /// </summary>
    public string? AdSenseSlotId { get; set; }

    /// <summary>
    /// Gets or sets the amount of seconds to keep the interstitial visible before enabling continue.
    /// </summary>
    public int AdSenseHoldSeconds { get; set; } = 8;

    /// <summary>
    /// Gets or sets a value indicating whether AdSense should appear on the login screen.
    /// </summary>
    public bool AdSenseShowOnLogin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether AdSense should appear on the home screen.
    /// </summary>
    public bool AdSenseShowOnHome { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether AdSense should appear after intros.
    /// </summary>
    public bool AdSenseShowAfterIntro { get; set; } = true;
}
