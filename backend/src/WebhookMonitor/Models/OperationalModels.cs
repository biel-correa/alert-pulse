namespace WebhookMonitor.Models;

public record SimulateWebhookRequest(
    string EndpointId,
    int StatusCode,
    string Payload,
    int LatencyMs,
    string? ErrorMessage = null
);

public record SimulateWebhookResponse(
    string EventId,
    string Status,
    string Severity
);

public record AcknowledgeIncidentResponse(
    string EventId,
    string Status,
    DateTimeOffset AcknowledgedAt
);

public record RetryIncidentResponse(
    string Status,
    string EventId,
    int TargetResponseStatusCode
);

public record CreateEndpointRequest(
    string Id,
    string Name,
    string? SeverityThreshold = "DEFAULT"
);

public record CreateEndpointResponse(
    string Id,
    string Name,
    string SecretKey,
    string UrlPath,
    DateTimeOffset CreatedAt
);

public record EndpointSummaryResponse(
    string Id,
    string Name,
    string UrlPath,
    string SeverityThreshold,
    DateTimeOffset CreatedAt,
    bool IsActive
);
