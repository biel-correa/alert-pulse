using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using WebhookMonitor.Configuration;
using WebhookMonitor.Models;

namespace WebhookMonitor.Services;

public interface IEndpointRepository
{
    Task<EndpointConfig?> GetEndpointAsync(string endpointId, CancellationToken ct = default);
    Task SaveEndpointAsync(EndpointConfig endpoint, CancellationToken ct = default);
    Task<IEnumerable<EndpointSummaryResponse>> GetAllAsync(CancellationToken ct = default);
    Task<CreateEndpointResponse> CreateAsync(CreateEndpointRequest request, CancellationToken ct = default);
}

public class EndpointRepository(IAmazonDynamoDB dynamoDb, IOptions<AwsOptions> options) : IEndpointRepository
{
    private readonly string _tableName = options.Value.DynamoDbTable;

    public async Task<EndpointConfig?> GetEndpointAsync(string endpointId, CancellationToken ct = default)
    {
        var request = new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new($"ENDPOINT#{endpointId}"),
                ["SK"] = new("METADATA")
            }
        };

        var response = await dynamoDb.GetItemAsync(request, ct);
        if (response.Item == null || response.Item.Count == 0)
        {
            return null;
        }

        var item = response.Item;
        var id = item.TryGetValue("Id", out var idVal) ? idVal.S : endpointId;
        var name = item.TryGetValue("Name", out var nameVal) ? nameVal.S : endpointId;
        var secretKey = item.TryGetValue("SecretKey", out var secretVal) ? secretVal.S : string.Empty;
        var urlPath = item.TryGetValue("UrlPath", out var urlVal) ? urlVal.S : $"/api/v1/webhooks/ingest/{endpointId}";
        var severityThreshold = item.TryGetValue("SeverityThreshold", out var sevVal) ? sevVal.S : "DEFAULT";
        var createdAtStr = item.TryGetValue("CreatedAt", out var catVal) ? catVal.S : DateTimeOffset.UtcNow.ToString("o");
        var isActive = !item.TryGetValue("IsActive", out var activeVal) || activeVal.BOOL == true;

        DateTimeOffset.TryParse(createdAtStr, out var createdAt);

        return new EndpointConfig(id, name, secretKey, urlPath, severityThreshold, createdAt, isActive);
    }

    public async Task SaveEndpointAsync(EndpointConfig endpoint, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new($"ENDPOINT#{endpoint.Id}"),
            ["SK"] = new("METADATA"),
            ["Id"] = new(endpoint.Id),
            ["Name"] = new(endpoint.Name),
            ["SecretKey"] = new(endpoint.SecretKey),
            ["UrlPath"] = new(endpoint.UrlPath),
            ["SeverityThreshold"] = new(endpoint.SeverityThreshold),
            ["CreatedAt"] = new(endpoint.CreatedAt.ToString("o")),
            ["IsActive"] = new AttributeValue { BOOL = endpoint.IsActive }
        };

        await dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = item
        }, ct);
    }

    public async Task<IEnumerable<EndpointSummaryResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var scanRequest = new ScanRequest
        {
            TableName = _tableName,
            FilterExpression = "SK = :sk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":sk"] = new("METADATA")
            }
        };

        var response = await dynamoDb.ScanAsync(scanRequest, ct);
        var results = new List<EndpointSummaryResponse>();

        foreach (var item in response.Items)
        {
            var id = item.TryGetValue("Id", out var idVal) ? idVal.S : string.Empty;
            var name = item.TryGetValue("Name", out var nameVal) ? nameVal.S : id;
            var urlPath = item.TryGetValue("UrlPath", out var urlVal) ? urlVal.S : $"/api/v1/webhooks/ingest/{id}";
            var severityThreshold = item.TryGetValue("SeverityThreshold", out var sevVal) ? sevVal.S : "DEFAULT";
            var createdAtStr = item.TryGetValue("CreatedAt", out var catVal) ? catVal.S : DateTimeOffset.UtcNow.ToString("o");
            var isActive = !item.TryGetValue("IsActive", out var activeVal) || activeVal.BOOL == true;

            DateTimeOffset.TryParse(createdAtStr, out var createdAt);

            results.Add(new EndpointSummaryResponse(id, name, urlPath, severityThreshold, createdAt, isActive));
        }

        return results;
    }

    public async Task<CreateEndpointResponse> CreateAsync(CreateEndpointRequest request, CancellationToken ct = default)
    {
        var secretKey = $"whsec_{Guid.NewGuid():N}";
        var urlPath = $"/api/v1/webhooks/ingest/{request.Id}";
        var now = DateTimeOffset.UtcNow;
        var severityThreshold = string.IsNullOrWhiteSpace(request.SeverityThreshold) ? "DEFAULT" : request.SeverityThreshold;

        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new($"ENDPOINT#{request.Id}"),
            ["SK"] = new("METADATA"),
            ["Id"] = new(request.Id),
            ["Name"] = new(request.Name),
            ["SecretKey"] = new(secretKey),
            ["UrlPath"] = new(urlPath),
            ["SeverityThreshold"] = new(severityThreshold),
            ["CreatedAt"] = new(now.ToString("o")),
            ["IsActive"] = new AttributeValue { BOOL = true }
        };

        await dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = item
        }, ct);

        return new CreateEndpointResponse(request.Id, request.Name, secretKey, urlPath, now);
    }
}
