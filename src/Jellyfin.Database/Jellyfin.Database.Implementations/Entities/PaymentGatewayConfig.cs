using System;

namespace MulletaFlix.Database.Implementations.Entities;

public class PaymentGatewayConfig
{
    public int Id { get; set; }
    public string GatewayName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsPrimary { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public bool SandboxMode { get; set; }
    public bool EnablePix { get; set; }
    public bool EnableCredit { get; set; }
    public bool EnableDebit { get; set; }
    public string? ExtraConfig { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
