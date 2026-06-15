using System;

namespace MulletaFlix.Database.Implementations.Entities;

public class DiscountCoupon
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountFixed { get; set; }
    public int? MaxUses { get; set; }
    public int CurrentUses { get; set; }
    public DateTime ValidFrom { get; set; } = DateTime.UtcNow;
    public DateTime? ValidUntil { get; set; }
    public bool IsActive { get; set; }
    public bool FirstPurchaseOnly { get; set; }
    public int? MinPlanMonths { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
