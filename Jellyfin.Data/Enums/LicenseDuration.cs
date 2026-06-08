namespace MulletaFlix.Data.Enums;

/// <summary>
/// Predefined license duration values in hours.
/// </summary>
public enum LicenseDuration
{
    /// <summary>
    /// No license duration specified.
    /// </summary>
    None = 0,

    /// <summary>
    /// Trial license of 1 hour.
    /// </summary>
    Trial = 1,

    /// <summary>
    /// 1 month license (~730 hours).
    /// </summary>
    OneMonth = 730,

    /// <summary>
    /// 3 months license (~2190 hours).
    /// </summary>
    ThreeMonths = 2190,

    /// <summary>
    /// 6 months license (~4380 hours).
    /// </summary>
    SixMonths = 4380,

    /// <summary>
    /// 12 months license (~8760 hours).
    /// </summary>
    TwelveMonths = 8760,

    /// <summary>
    /// Unlimited license (never expires).
    /// </summary>
    Unlimited = -1
}

