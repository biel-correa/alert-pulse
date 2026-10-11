using WebhookMonitor.Models;
using WebhookMonitor.Services;

namespace WebhookMonitor.Endpoints;

public static class OperationalEndpoints
{
    public static IEndpointRouteBuilder MapOperationalEndpoints(this IEndpointRouteBuilder app)
    {
        // 1. Synthetic Webhook Simulation
        app.MapPost("/api/v1/webhooks/simulate", async (
            SimulateWebhookRequest request,
            IWebhookIngestionService ingestionService,
            CancellationToken ct) =>
        {
            var result = await ingestionService.SimulateAsync(request, ct);
            return Results.Ok(result);
        })
        .WithName("SimulateWebhook")
        .WithSummary("Generate synthetic webhook events for end-to-end pipeline verification")
        .RequireRateLimiting("IngestionRateLimit")
        .Produces<SimulateWebhookResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status429TooManyRequests);

        // 2. Incident Acknowledge
        app.MapPost("/api/v1/incidents/{endpointId}/{eventId}/acknowledge", async (
            string endpointId,
            string eventId,
            IIncidentService incidentService,
            CancellationToken ct) =>
        {
            var result = await incidentService.AcknowledgeAsync(endpointId, eventId, ct);
            if (result == null)
            {
                return Results.NotFound(new { message = $"Incident {eventId} on endpoint {endpointId} was not found." });
            }

            return Results.Ok(result);
        })
        .WithName("AcknowledgeIncident")
        .WithSummary("Acknowledge an open webhook incident")
        .Produces<AcknowledgeIncidentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // 3. Incident Replay / Retry
        app.MapPost("/api/v1/incidents/{endpointId}/{eventId}/retry", async (
            string endpointId,
            string eventId,
            IIncidentService incidentService,
            CancellationToken ct) =>
        {
            var result = await incidentService.RetryAsync(endpointId, eventId, ct);
            if (result == null)
            {
                return Results.NotFound(new { message = $"Incident {eventId} on endpoint {endpointId} was not found." });
            }

            return Results.Ok(result);
        })
        .WithName("RetryIncident")
        .WithSummary("Replay raw webhook payload against destination service")
        .Produces<RetryIncidentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // 4. Endpoint Configuration CRUD
        app.MapGet("/api/v1/endpoints", async (
            IEndpointRepository repository,
            CancellationToken ct) =>
        {
            var endpoints = await repository.GetAllAsync(ct);
            return Results.Ok(endpoints);
        })
        .WithName("GetAllEndpoints")
        .WithSummary("List all registered webhook endpoint configurations")
        .Produces<IEnumerable<EndpointSummaryResponse>>(StatusCodes.Status200OK);

        app.MapPost("/api/v1/endpoints", async (
            CreateEndpointRequest request,
            IEndpointRepository repository,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Endpoint Id and Name are required." });
            }

            var result = await repository.CreateAsync(request, ct);
            return Results.Created($"/api/v1/webhooks/ingest/{result.Id}", result);
        })
        .WithName("CreateEndpoint")
        .WithSummary("Register a new webhook endpoint with auto-generated HMAC secret key")
        .Produces<CreateEndpointResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        return app;
    }
}
