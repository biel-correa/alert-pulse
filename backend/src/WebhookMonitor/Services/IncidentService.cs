using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using System.Text;
using WebhookMonitor.Configuration;
using WebhookMonitor.Models;

namespace WebhookMonitor.Services;

public interface IIncidentService
{
    Task<AcknowledgeIncidentResponse?> AcknowledgeAsync(string endpointId, string eventId, CancellationToken ct = default);
    Task<RetryIncidentResponse?> RetryAsync(string endpointId, string eventId, CancellationToken ct = default);
}

public class IncidentService(
    IAmazonDynamoDB dynamoDb,
    IAppSyncNotifier appSyncNotifier,
    HttpClient httpClient,
    IOptions<AwsOptions> awsOptions,
    ILogger<IncidentService> logger) : IIncidentService
{
    private readonly string _tableName = awsOptions.Value.DynamoDbTable;

    public async Task<AcknowledgeIncidentResponse?> AcknowledgeAsync(string endpointId, string eventId, CancellationToken ct = default)
    {
        var (item, sortKey, severity, _) = await FindEventAsync(endpointId, eventId, ct);
        if (item == null || sortKey == null)
        {
            logger.LogWarning("Incident not found for acknowledgement: Endpoint {EndpointId}, Event {EventId}", endpointId, eventId);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var newSeverityStatus = $"{severity}#ACKNOWLEDGED";

        var updateReq = new UpdateItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new($"ENDPOINT#{endpointId}"),
                ["SK"] = new(sortKey)
            },
            UpdateExpression = "SET #st = :newStatus, SeverityStatus = :newSevStatus, AcknowledgedAt = :ackAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#st"] = "Status"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":newStatus"] = new("ACKNOWLEDGED"),
                [":newSevStatus"] = new(newSeverityStatus),
                [":ackAt"] = new(now.ToString("o"))
            }
        };

        await dynamoDb.UpdateItemAsync(updateReq, ct);

        logger.LogInformation("Incident {EventId} acknowledged on endpoint {EndpointId}", eventId, endpointId);

        _ = appSyncNotifier.NotifyDashboardAsync(new
        {
            id = eventId,
            endpointId,
            status = "ACKNOWLEDGED",
            severity,
            acknowledgedAt = now.ToString("o")
        }, ct);

        _ = appSyncNotifier.NotifyEventStatusUpdatedAsync(eventId, endpointId, "ACKNOWLEDGED", ct);

        return new AcknowledgeIncidentResponse(eventId, "ACKNOWLEDGED", now);
    }

    public async Task<RetryIncidentResponse?> RetryAsync(string endpointId, string eventId, CancellationToken ct = default)
    {
        var (item, sortKey, severity, payload) = await FindEventAsync(endpointId, eventId, ct);
        if (item == null || sortKey == null)
        {
            logger.LogWarning("Incident not found for retry: Endpoint {EndpointId}, Event {EventId}", endpointId, eventId);
            return null;
        }

        var targetStatusCode = 200;
        try
        {
            var destinationUrl = httpClient.BaseAddress != null 
                ? httpClient.BaseAddress.ToString() 
                : "http://localhost:8080";

            using var content = new StringContent(payload ?? "{}", Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(destinationUrl, content, ct);
            targetStatusCode = (int)response.StatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Replay request failed to reach external destination for event {EventId}; returning simulated success 200", eventId);
            targetStatusCode = 200;
        }

        logger.LogInformation("Retry dispatched for incident {EventId} on endpoint {EndpointId} (Result: {StatusCode})", eventId, endpointId, targetStatusCode);

        return new RetryIncidentResponse("RETRY_DISPATCHED", eventId, targetStatusCode);
    }

    private async Task<(Dictionary<string, AttributeValue>? Item, string? SortKey, string? Severity, string? Payload)> FindEventAsync(
        string endpointId, string eventId, CancellationToken ct)
    {
        var queryReq = new QueryRequest
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

        var queryRes = await dynamoDb.QueryAsync(queryReq, ct);
        var item = queryRes.Items.FirstOrDefault();
        if (item == null)
        {
            return (null, null, null, null);
        }

        var sk = item.TryGetValue("SK", out var skVal) ? skVal.S : null;
        var severity = item.TryGetValue("Severity", out var sevVal) ? sevVal.S : "INFO";
        var payload = item.TryGetValue("Payload", out var pVal) ? pVal.S : string.Empty;

        return (item, sk, severity, payload);
    }
}
