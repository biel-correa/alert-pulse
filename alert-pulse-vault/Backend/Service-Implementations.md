---
title: "C# Service Implementations (.NET 10)"
tags:
  - backend
  - dotnet
  - csharp
  - code
  - dynamodb
  - sns
  - appsync
date: 2026-10-11
status: approved
---

# C# Service Implementations (.NET 10)

This document contains production-ready reference implementations for the core backend services powering the .NET 10 API of **Alert Pulse**.

---

## 1. Ingestion Engine: `WebhookIngestionService.cs`

```csharp
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebhookMonitor.Services;

public record IngestWebhookCommand(
    string EndpointId, 
    int StatusCode, 
    string Payload, 
    int LatencyMs, 
    string? ErrorMessage
);

public class WebhookIngestionService(
    IAmazonDynamoDB dynamoDb,
    IAmazonSimpleNotificationService snsClient,
    IAppSyncNotifier appSyncNotifier,
    IConfiguration configuration,
    ILogger<WebhookIngestionService> logger)
{
    private readonly string _tableName = configuration["AWS:DynamoDbTable"] 
        ?? throw new InvalidOperationException("AWS:DynamoDbTable is not configured.");
    private readonly string _snsTopicArn = configuration["AWS:SnsAlertTopicArn"] 
        ?? throw new InvalidOperationException("AWS:SnsAlertTopicArn is not configured.");

    public async Task<string> ProcessAsync(IngestWebhookCommand command, CancellationToken ct = default)
    {
        var eventId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var severity = ClassifySeverity(command.StatusCode, command.LatencyMs);
        var expiresAt = now.AddDays(30).ToUnixTimeSeconds();

        logger.LogInformation("Processing webhook {EventId} for {EndpointId}. Severity: {Severity}",
            eventId, command.EndpointId, severity);

        // 1. Persist to DynamoDB (Single-Table Design with TTL)
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
            TableName = _tableName,
            Item = item
        }, ct);

        // 2. If CRITICAL, fan-out to SNS (Email/SMS/PagerDuty/SQS)
        if (severity == "CRITICAL")
        {
            logger.LogWarning("Triggering critical SNS alert for {EndpointId} (EventId: {EventId})", 
                command.EndpointId, eventId);

            var snsMessage = JsonSerializer.Serialize(new
            {
                EventId = eventId,
                command.EndpointId,
                command.StatusCode,
                command.LatencyMs,
                command.ErrorMessage,
                Timestamp = now
            });

            await snsClient.PublishAsync(new PublishRequest
            {
                TopicArn = _snsTopicArn,
                Subject = $"[CRITICAL ALERT] Webhook Failure on {command.EndpointId}",
                Message = snsMessage,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["Severity"] = new() { DataType = "String", StringValue = "CRITICAL" }
                }
            }, ct);
        }

        // 3. Notify connected AppSync clients via GraphQL Mutation
        try
        {
            await appSyncNotifier.NotifyDashboardAsync(new
            {
                id = eventId,
                endpointId = command.EndpointId,
                timestamp = now.ToString("o"),
                statusCode = command.StatusCode,
                latencyMs = command.LatencyMs,
                severity,
                status = "OPEN",
                errorMessage = command.ErrorMessage,
                payloadSnippet = command.Payload.Length > 150 
                    ? command.Payload[..150] + "..." 
                    : command.Payload
            }, ct);
        }
        catch (Exception ex)
        {
            // AppSync failure does not invalidate successful persistence or SNS alert
            logger.LogError(ex, "Failed to send real-time notification to AppSync.");
        }

        return eventId;
    }

    public static string ClassifySeverity(int statusCode, int latencyMs) =>
        (statusCode >= 500 || latencyMs > 5000) ? "CRITICAL" :
        (statusCode >= 400 || latencyMs > 2000) ? "WARNING" : "INFO";
}
```

---

## 2. Cryptographic Validation: `HmacValidationService.cs`

```csharp
using System.Security.Cryptography;
using System.Text;

namespace WebhookMonitor.Infrastructure.Security;

public interface IHmacValidationService
{
    bool IsValidSignature(byte[] bodyBytes, string? signatureHeader, string secretKey);
}

public class HmacValidationService : IHmacValidationService
{
    private const string Sha256Prefix = "sha256=";

    public bool IsValidSignature(byte[] bodyBytes, string? signatureHeader, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith(Sha256Prefix))
        {
            return false;
        }

        var expectedHex = signatureHeader[Sha256Prefix.Length..];
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(bodyBytes);
        var computedHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var expectedBytes = Encoding.UTF8.GetBytes(expectedHex.ToLowerInvariant());
        var actualBytes = Encoding.UTF8.GetBytes(computedHex);

        // Prevents timing attacks with constant-time equality comparison
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
```

---

## 3. Real-Time Notifier: `HttpAppSyncNotifier.cs`

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace WebhookMonitor.Infrastructure.Notifications;

public interface IAppSyncNotifier
{
    Task NotifyDashboardAsync(object eventInput, CancellationToken ct = default);
}

public class HttpAppSyncNotifier(HttpClient httpClient, IConfiguration configuration) : IAppSyncNotifier
{
    private readonly string _endpoint = configuration["AppSync:GraphQLEndpoint"]!;
    private readonly string _apiKey = configuration["AppSync:ApiKey"]!;

    public async Task NotifyDashboardAsync(object eventInput, CancellationToken ct = default)
    {
        var mutation = @"
            mutation Publish($input: WebhookEventInput!) {
                publishWebhookEvent(event: $input) {
                    id
                    endpointId
                    timestamp
                    statusCode
                    latencyMs
                    severity
                    status
                    errorMessage
                }
            }";

        var payload = new
        {
            query = mutation,
            variables = new { input = eventInput }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        request.Headers.Add("x-api-key", _apiKey);

        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
```

---

## 4. Incident Management: `IncidentManagementService.cs`

```csharp
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Configuration;

namespace WebhookMonitor.Services;

public class IncidentManagementService(IAmazonDynamoDB dynamoDb, IConfiguration configuration)
{
    private readonly string _tableName = configuration["AWS:DynamoDbTable"]!;

    public async Task<bool> AcknowledgeAsync(string endpointId, string eventId, CancellationToken ct = default)
    {
        // 1. Locate item SK using prefix query
        var query = new QueryRequest
        {
            TableName = _tableName,
            KeyConditionExpression = "PK = :pk AND begins_with(SK, :skPrefix)",
            FilterExpression = "Id = :id",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"ENDPOINT#{endpointId}"),
                [":skPrefix"] = new("EVENT#"),
                [":id"] = new(eventId)
            }
        };

        var queryResponse = await dynamoDb.QueryAsync(query, ct);
        if (queryResponse.Items.Count == 0) return false;

        var targetSk = queryResponse.Items[0]["SK"].S;
        var severity = queryResponse.Items[0]["Severity"].S;

        // 2. Update Status and SeverityStatus
        await dynamoDb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new($"ENDPOINT#{endpointId}"),
                ["SK"] = new(targetSk)
            },
            UpdateExpression = "SET #status = :ack, SeverityStatus = :sevAck",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = "Status"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":ack"] = new("ACKNOWLEDGED"),
                [":sevAck"] = new($"{severity}#ACKNOWLEDGED")
            }
        }, ct);

        return true;
    }
}
```

---

## 🔗 Related Notes
* Backend architecture overview: [[DotNet-Architecture|.NET 10 Web API Architecture]].
* REST endpoint contracts: [[Operational-Endpoints|REST Endpoints Catalog]].
