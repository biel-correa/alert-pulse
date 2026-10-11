using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebhookMonitor.Configuration;
using WebhookMonitor.Services;
using Xunit;

namespace WebhookMonitor.Tests;

public class IncidentServiceTests
{
    private readonly IAmazonDynamoDB _dynamoDb = Substitute.For<IAmazonDynamoDB>();
    private readonly IAppSyncNotifier _appSyncNotifier = Substitute.For<IAppSyncNotifier>();
    private readonly HttpClient _httpClient = new HttpClient();
    private readonly IOptions<AwsOptions> _awsOptions = Options.Create(new AwsOptions
    {
        DynamoDbTable = "MonitoringHub"
    });
    private readonly ILogger<IncidentService> _logger = Substitute.For<ILogger<IncidentService>>();

    private readonly IncidentService _sut;

    public IncidentServiceTests()
    {
        _sut = new IncidentService(
            _dynamoDb,
            _appSyncNotifier,
            _httpClient,
            _awsOptions,
            _logger);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenIncidentExists_UpdatesDynamoDbAndNotifiesAppSync()
    {
        // Arrange
        var endpointId = "stripe-billing";
        var eventId = "evt_12345";

        _dynamoDb.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                    new()
                    {
                        ["PK"] = new($"ENDPOINT#{endpointId}"),
                        ["SK"] = new("EVENT#1791724286000#evt_12345"),
                        ["Id"] = new(eventId),
                        ["Severity"] = new("CRITICAL"),
                        ["Status"] = new("OPEN"),
                        ["SeverityStatus"] = new("CRITICAL#OPEN")
                    }
                }
            });

        UpdateItemRequest? capturedUpdate = null;
        await _dynamoDb.UpdateItemAsync(Arg.Do<UpdateItemRequest>(req => capturedUpdate = req), Arg.Any<CancellationToken>());

        // Act
        var result = await _sut.AcknowledgeAsync(endpointId, eventId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal("ACKNOWLEDGED", result.Status);

        Assert.NotNull(capturedUpdate);
        Assert.Equal("ENDPOINT#stripe-billing", capturedUpdate.Key["PK"].S);
        Assert.Equal("EVENT#1791724286000#evt_12345", capturedUpdate.Key["SK"].S);
        Assert.Equal("ACKNOWLEDGED", capturedUpdate.ExpressionAttributeValues[":newStatus"].S);
        Assert.Equal("CRITICAL#ACKNOWLEDGED", capturedUpdate.ExpressionAttributeValues[":newSevStatus"].S);

        await _appSyncNotifier.Received(1).NotifyDashboardAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenIncidentNotFound_ReturnsNull()
    {
        // Arrange
        _dynamoDb.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new QueryResponse { Items = new List<Dictionary<string, AttributeValue>>() });

        // Act
        var result = await _sut.AcknowledgeAsync("stripe-billing", "non_existing");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RetryAsync_WhenIncidentExists_ReturnsSuccessDispatched()
    {
        // Arrange
        var endpointId = "stripe-billing";
        var eventId = "evt_12345";

        _dynamoDb.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                    new()
                    {
                        ["PK"] = new($"ENDPOINT#{endpointId}"),
                        ["SK"] = new("EVENT#1791724286000#evt_12345"),
                        ["Id"] = new(eventId),
                        ["Severity"] = new("CRITICAL"),
                        ["Payload"] = new("{\"retry\":true}")
                    }
                }
            });

        // Act
        var result = await _sut.RetryAsync(endpointId, eventId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("RETRY_DISPATCHED", result.Status);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(200, result.TargetResponseStatusCode);
    }

    [Fact]
    public async Task RetryAsync_WhenIncidentNotFound_ReturnsNull()
    {
        // Arrange
        _dynamoDb.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new QueryResponse { Items = new List<Dictionary<string, AttributeValue>>() });

        // Act
        var result = await _sut.RetryAsync("stripe-billing", "missing");

        // Assert
        Assert.Null(result);
    }
}
