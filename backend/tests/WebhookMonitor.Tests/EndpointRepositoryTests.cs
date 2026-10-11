using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebhookMonitor.Configuration;
using WebhookMonitor.Models;
using WebhookMonitor.Services;
using Xunit;

namespace WebhookMonitor.Tests;

public class EndpointRepositoryTests
{
    private readonly IAmazonDynamoDB _dynamoDb = Substitute.For<IAmazonDynamoDB>();
    private readonly IOptions<AwsOptions> _options = Options.Create(new AwsOptions
    {
        DynamoDbTable = "MonitoringHub"
    });

    private readonly EndpointRepository _sut;

    public EndpointRepositoryTests()
    {
        _sut = new EndpointRepository(_dynamoDb, _options);
    }

    [Fact]
    public async Task GetEndpointAsync_WhenFound_ReturnsConfig()
    {
        // Arrange
        _dynamoDb.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new("stripe-billing"),
                    ["Name"] = new("Stripe Billing"),
                    ["SecretKey"] = new("whsec_secret_123"),
                    ["UrlPath"] = new("/api/v1/webhooks/ingest/stripe-billing"),
                    ["SeverityThreshold"] = new("DEFAULT"),
                    ["CreatedAt"] = new(DateTimeOffset.UtcNow.ToString("o")),
                    ["IsActive"] = new AttributeValue { BOOL = true }
                }
            });

        // Act
        var result = await _sut.GetEndpointAsync("stripe-billing");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("stripe-billing", result.Id);
        Assert.Equal("Stripe Billing", result.Name);
        Assert.Equal("whsec_secret_123", result.SecretKey);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task GetEndpointAsync_WhenNotFound_ReturnsNull()
    {
        // Arrange
        _dynamoDb.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetItemResponse { Item = new Dictionary<string, AttributeValue>() });

        // Act
        var result = await _sut.GetEndpointAsync("non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ShouldScanAndReturnEndpoints()
    {
        // Arrange
        _dynamoDb.ScanAsync(Arg.Any<ScanRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ScanResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                    new()
                    {
                        ["Id"] = new("stripe-billing"),
                        ["Name"] = new("Stripe Billing"),
                        ["UrlPath"] = new("/api/v1/webhooks/ingest/stripe-billing"),
                        ["SeverityThreshold"] = new("DEFAULT"),
                        ["CreatedAt"] = new("2026-10-11T10:00:00Z"),
                        ["IsActive"] = new AttributeValue { BOOL = true }
                    },
                    new()
                    {
                        ["Id"] = new("github-deployments"),
                        ["Name"] = new("GitHub CI/CD"),
                        ["UrlPath"] = new("/api/v1/webhooks/ingest/github-deployments"),
                        ["SeverityThreshold"] = new("DEFAULT"),
                        ["CreatedAt"] = new("2026-10-11T10:00:00Z"),
                        ["IsActive"] = new AttributeValue { BOOL = true }
                    }
                }
            });

        // Act
        var results = (await _sut.GetAllAsync()).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("stripe-billing", results[0].Id);
        Assert.Equal("github-deployments", results[1].Id);
    }

    [Fact]
    public async Task CreateAsync_ShouldGenerateSecretAndPersistItem()
    {
        // Arrange
        var request = new CreateEndpointRequest("custom-api", "Custom API Webhook");
        PutItemRequest? capturedPut = null;
        await _dynamoDb.PutItemAsync(Arg.Do<PutItemRequest>(req => capturedPut = req), Arg.Any<CancellationToken>());

        // Act
        var response = await _sut.CreateAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("custom-api", response.Id);
        Assert.Equal("Custom API Webhook", response.Name);
        Assert.StartsWith("whsec_", response.SecretKey);
        Assert.Equal("/api/v1/webhooks/ingest/custom-api", response.UrlPath);

        Assert.NotNull(capturedPut);
        Assert.Equal("ENDPOINT#custom-api", capturedPut.Item["PK"].S);
        Assert.Equal("METADATA", capturedPut.Item["SK"].S);
        Assert.Equal(response.SecretKey, capturedPut.Item["SecretKey"].S);
    }
}
