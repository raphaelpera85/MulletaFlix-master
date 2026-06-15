using System;

namespace MulletaFlix.Database.Implementations.Entities;

public class PricingPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationMonths { get; set; }
    public decimal PricePerMonth { get; set; }
    public decimal TotalPrice { get; set; }
    public bool IsActive { get; set; }
    public bool IsHighlighted { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
