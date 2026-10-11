namespace WebhookMonitor.Models;

public record IngestWebhookCommand(
    string EndpointId,
    int StatusCode,
    string Payload,
    int LatencyMs,
    string? ErrorMessage = null
);

public record IngestWebhookResponse(
    string EventId,
    string EndpointId,
    string Status,
    string Severity,
    DateTimeOffset ProcessedAt
);
