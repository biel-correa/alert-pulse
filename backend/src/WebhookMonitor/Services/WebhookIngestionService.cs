using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebhookMonitor.Configuration;
using WebhookMonitor.Models;

namespace WebhookMonitor.Services;

public interface IWebhookIngestionService
{
    Task<IngestWebhookResponse> ProcessAsync(IngestWebhookCommand command, CancellationToken ct = default);
    Task<SimulateWebhookResponse> SimulateAsync(SimulateWebhookRequest request, CancellationToken ct = default);
}

public class WebhookIngestionService(
    IAmazonDynamoDB dynamoDb,
    IAmazonSimpleNotificationService snsClient,
    ISeverityClassifier severityClassifier,
    IAppSyncNotifier appSyncNotifier,
    IOptions<AwsOptions> awsOptions,
    ILogger<WebhookIngestionService> logger) : IWebhookIngestionService
{
    private readonly AwsOptions _options = awsOptions.Value;

    public async Task<IngestWebhookResponse> ProcessAsync(IngestWebhookCommand command, CancellationToken ct = default)
    {
        var eventId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var severity = severityClassifier.Classify(command.StatusCode, command.LatencyMs);
        var expiresAt = now.AddDays(30).ToUnixTimeSeconds();

        logger.LogInformation(
            "Processing webhook event {EventId} for endpoint {EndpointId}. Status: {StatusCode}, Latency: {LatencyMs}ms, Severity: {Severity}",
            eventId, command.EndpointId, command.StatusCode, command.LatencyMs, severity);

        // 1. Mandatory Persistence in DynamoDB (Single-Table Design with TTL)
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new($"ENDPOINT#{command.EndpointId}"),
            ["SK"] = new($"EVENT#{now.ToUnixTimeMilliseconds()}#{eventId}"),
            ["Id"] = new(eventId),
            ["EndpointId"] = new(command.EndpointId),
            ["StatusCode"] = new AttributeValue { N = command.StatusCode.ToString() },
            ["LatencyMs"] = new AttributeValue { N = command.LatencyMs.ToString() },
            ["Severity"] = new(severity),
            ["Status"] = new("OPEN"),
            ["SeverityStatus"] = new($"{severity}#OPEN"),
            ["CreatedAt"] = new(now.ToString("o")),
            ["ExpiresAt"] = new AttributeValue { N = expiresAt.ToString() },
            ["Payload"] = new(command.Payload)
        };

        if (!string.IsNullOrEmpty(command.ErrorMessage))
        {
            item["ErrorMessage"] = new(command.ErrorMessage);
        }

        await dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _options.DynamoDbTable,
            Item = item
        }, ct);

        // 2. Asynchronous Fan-Out Alerting via AWS SNS for CRITICAL Events
        if (severity == SeverityClassifier.Critical)
        {
            logger.LogWarning("Dispatching critical SNS alert for event {EventId} on endpoint {EndpointId}", eventId, command.EndpointId);

            var snsPayload = JsonSerializer.Serialize(new
            {
                EventId = eventId,
                command.EndpointId,
                command.StatusCode,
                command.LatencyMs,
                command.ErrorMessage,
                Timestamp = now
            });

            try
            {
                await snsClient.PublishAsync(new PublishRequest
                {
                    TopicArn = _options.SnsAlertTopicArn,
                    Subject = $"[CRITICAL ALERT] Webhook Failure on {command.EndpointId}",
                    Message = snsPayload,
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        ["Severity"] = new() { DataType = "String", StringValue = SeverityClassifier.Critical }
                    }
                }, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish critical alert to SNS topic {TopicArn}", _options.SnsAlertTopicArn);
            }
        }

        // 3. Real-Time Telemetry Push via AppSync GraphQL Mutation
        var payloadSnippet = command.Payload.Length > 150
            ? string.Concat(command.Payload.AsSpan(0, 150), "...")
            : command.Payload;

        _ = appSyncNotifier.NotifyDashboardAsync(new
        {
            id = eventId,
            endpointId = command.EndpointId,
            timestamp = now.ToString("o"),
            statusCode = command.StatusCode,
            latencyMs = command.LatencyMs,
            severity,
            status = "OPEN",
            errorMessage = command.ErrorMessage,
            payloadSnippet
        }, ct);

        return new IngestWebhookResponse(eventId, command.EndpointId, "PROCESSED", severity, now);
    }

    public async Task<SimulateWebhookResponse> SimulateAsync(SimulateWebhookRequest request, CancellationToken ct = default)
    {
        var eventId = $"sim_{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var severity = severityClassifier.Classify(request.StatusCode, request.LatencyMs);
        var expiresAt = now.AddDays(30).ToUnixTimeSeconds();

        logger.LogInformation(
            "Simulating webhook event {EventId} for endpoint {EndpointId}. Status: {StatusCode}, Latency: {LatencyMs}ms, Severity: {Severity}",
            eventId, request.EndpointId, request.StatusCode, request.LatencyMs, severity);

        // 1. Persist in DynamoDB (Single-Table Design with TTL)
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new($"ENDPOINT#{request.EndpointId}"),
            ["SK"] = new($"EVENT#{now.ToUnixTimeMilliseconds()}#{eventId}"),
            ["Id"] = new(eventId),
            ["EndpointId"] = new(request.EndpointId),
            ["StatusCode"] = new AttributeValue { N = request.StatusCode.ToString() },
            ["LatencyMs"] = new AttributeValue { N = request.LatencyMs.ToString() },
            ["Severity"] = new(severity),
            ["Status"] = new("OPEN"),
            ["SeverityStatus"] = new($"{severity}#OPEN"),
            ["CreatedAt"] = new(now.ToString("o")),
            ["ExpiresAt"] = new AttributeValue { N = expiresAt.ToString() },
            ["Payload"] = new(request.Payload)
        };

        if (!string.IsNullOrEmpty(request.ErrorMessage))
        {
            item["ErrorMessage"] = new(request.ErrorMessage);
        }

        await dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _options.DynamoDbTable,
            Item = item
        }, ct);

        // 2. Fan-Out Alerting via SNS for CRITICAL Events
        if (severity == SeverityClassifier.Critical)
        {
            logger.LogWarning("Dispatching simulated critical SNS alert for event {EventId} on endpoint {EndpointId}", eventId, request.EndpointId);

            var snsPayload = JsonSerializer.Serialize(new
            {
                EventId = eventId,
                request.EndpointId,
                request.StatusCode,
                request.LatencyMs,
                request.ErrorMessage,
                Timestamp = now
            });

            try
            {
                await snsClient.PublishAsync(new PublishRequest
                {
                    TopicArn = _options.SnsAlertTopicArn,
                    Subject = $"[SIMULATED CRITICAL ALERT] Webhook Failure on {request.EndpointId}",
                    Message = snsPayload,
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        ["Severity"] = new() { DataType = "String", StringValue = SeverityClassifier.Critical }
                    }
                }, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish simulated critical alert to SNS topic {TopicArn}", _options.SnsAlertTopicArn);
            }
        }

        // 3. AppSync Real-Time Telemetry Push
        var payloadSnippet = request.Payload.Length > 150
            ? string.Concat(request.Payload.AsSpan(0, 150), "...")
            : request.Payload;

        _ = appSyncNotifier.NotifyDashboardAsync(new
        {
            id = eventId,
            endpointId = request.EndpointId,
            timestamp = now.ToString("o"),
            statusCode = request.StatusCode,
            latencyMs = request.LatencyMs,
            severity,
            status = "OPEN",
            errorMessage = request.ErrorMessage,
            payloadSnippet
        }, ct);

        return new SimulateWebhookResponse(eventId, "SIMULATED", severity);
    }
}
