using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using WebhookMonitor.Models;
using WebhookMonitor.Services;

namespace WebhookMonitor.Endpoints;

public static class IngestionEndpoints
{
    public const string SignatureHeader = "X-Hub-Signature-256";

    public static IEndpointRouteBuilder MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/webhooks")
            .WithTags("Webhooks Ingestion");

        group.MapPost("/ingest/{endpointId}", async (
            string endpointId,
            HttpContext context,
            [FromServices] IEndpointRepository endpointRepo,
            [FromServices] IHmacValidationService hmacService,
            [FromServices] IWebhookIngestionService ingestionService,
            CancellationToken ct) =>
        {
            var stopwatch = Stopwatch.StartNew();

            // 1. Validate signature header presence
            if (!context.Request.Headers.TryGetValue(SignatureHeader, out var signatureHeader) ||
                string.IsNullOrWhiteSpace(signatureHeader))
            {
                return Results.Json(new
                {
                    error = "Missing required cryptographic signature header 'X-Hub-Signature-256'."
                }, statusCode: StatusCodes.Status401Unauthorized);
            }

            // 2. Retrieve endpoint metadata and secret key from DynamoDB
            var endpoint = await endpointRepo.GetEndpointAsync(endpointId, ct);
            if (endpoint == null || !endpoint.IsActive)
            {
                return Results.NotFound(new
                {
                    error = $"Endpoint '{endpointId}' was not found or is inactive."
                });
            }

            // 3. Read raw request payload bytes
            context.Request.EnableBuffering();
            using var memoryStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(memoryStream, ct);
            var bodyBytes = memoryStream.ToArray();

            // 4. Cryptographic HMAC-SHA256 signature verification
            var isValid = hmacService.IsValidSignature(bodyBytes, signatureHeader, endpoint.SecretKey);
            if (!isValid)
            {
                return Results.Json(new
                {
                    error = "Invalid cryptographic signature. Signature verification failed."
                }, statusCode: StatusCodes.Status401Unauthorized);
            }

            stopwatch.Stop();
            var latencyMs = (int)stopwatch.ElapsedMilliseconds;
            var payloadString = Encoding.UTF8.GetString(bodyBytes);

            // 5. Ingestion & classification pipeline execution
            var command = new IngestWebhookCommand(
                EndpointId: endpointId,
                StatusCode: StatusCodes.Status200OK,
                Payload: payloadString,
                LatencyMs: latencyMs
            );

            var result = await ingestionService.ProcessAsync(command, ct);

            return Results.Accepted($"/api/v1/incidents/{endpointId}/{result.EventId}", result);
        })
        .WithName("IngestWebhook")
        .WithSummary("Ingests an external webhook, validates its HMAC signature, and persists the event.")
        .WithDescription("Accepts external webhook payloads with an HMAC SHA-256 signature in the X-Hub-Signature-256 header.")
        .RequireRateLimiting("IngestionRateLimit")
        .Produces<IngestWebhookResponse>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status429TooManyRequests);

        return app;
    }
}
