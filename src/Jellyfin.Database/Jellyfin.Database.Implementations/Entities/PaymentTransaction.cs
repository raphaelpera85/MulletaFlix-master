using System;
using MulletaFlix.Database.Implementations.Enums;

namespace MulletaFlix.Database.Implementations.Entities;

public class PaymentTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public virtual User? User { get; set; }
    public int PricingPlanId { get; set; }
    public virtual PricingPlan? PricingPlan { get; set; }
    public string GatewayName { get; set; } = string.Empty;
    public string GatewayTransactionId { get; set; } = string.Empty;
    public PaymentMethod PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public decimal DiscountAmount { get; set; }
    public int? CouponId { get; set; }
    public virtual DiscountCoupon? Coupon { get; set; }
    public PaymentStatus Status { get; set; }
    public bool IsRecurring { get; set; }
    public string? RecurringSubscriptionId { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? GatewayResponse { get; set; }
}
