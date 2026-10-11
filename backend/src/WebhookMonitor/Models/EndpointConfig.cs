namespace WebhookMonitor.Models;

public record EndpointConfig(
    string Id,
    string Name,
    string SecretKey,
    string UrlPath,
    string SeverityThreshold,
    DateTimeOffset CreatedAt,
    bool IsActive = true
);
