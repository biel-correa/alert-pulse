using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebhookMonitor.Configuration;
using WebhookMonitor.Models;
using WebhookMonitor.Services;
using Xunit;

namespace WebhookMonitor.Tests;

public class WebhookIngestionServiceTests
{
    private readonly IAmazonDynamoDB _dynamoDb = Substitute.For<IAmazonDynamoDB>();
    private readonly IAmazonSimpleNotificationService _snsClient = Substitute.For<IAmazonSimpleNotificationService>();
    private readonly ISeverityClassifier _severityClassifier = Substitute.For<ISeverityClassifier>();
    private readonly IAppSyncNotifier _appSyncNotifier = Substitute.For<IAppSyncNotifier>();
    private readonly IOptions<AwsOptions> _awsOptions = Options.Create(new AwsOptions
    {
        DynamoDbTable = "MonitoringHub",
        SnsAlertTopicArn = "arn:aws:sns:us-east-1:000000000000:WebhookCriticalAlerts"
    });
    private readonly ILogger<WebhookIngestionService> _logger = Substitute.For<ILogger<WebhookIngestionService>>();

    private readonly WebhookIngestionService _sut;

    public WebhookIngestionServiceTests()
    {
        _sut = new WebhookIngestionService(
            _dynamoDb,
            _snsClient,
            _severityClassifier,
            _appSyncNotifier,
            _awsOptions,
            _logger);
    }

    [Fact]
    public async Task ProcessAsync_ShouldPersistItemToDynamoDb_WithCorrectAttributesAndTTL()
    {
        // Arrange
        var command = new IngestWebhookCommand("stripe-billing", 200, "{\"type\":\"payment_intent.succeeded\"}", 150);
        _severityClassifier.Classify(command.StatusCode, command.LatencyMs).Returns(SeverityClassifier.Info);

        PutItemRequest? capturedRequest = null;
        await _dynamoDb.PutItemAsync(Arg.Do<PutItemRequest>(req => capturedRequest = req), Arg.Any<CancellationToken>());

        var beforeCall = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        var result = await _sut.ProcessAsync(command);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("stripe-billing", result.EndpointId);
        Assert.Equal("PROCESSED", result.Status);
        Assert.Equal(SeverityClassifier.Info, result.Severity);

        Assert.NotNull(capturedRequest);
        Assert.Equal("MonitoringHub", capturedRequest.TableName);
        var item = capturedRequest.Item;

        Assert.Equal("ENDPOINT#stripe-billing", item["PK"].S);
        Assert.StartsWith("EVENT#", item["SK"].S);
        Assert.Equal(result.EventId, item["Id"].S);
        Assert.Equal("200", item["StatusCode"].N);
        Assert.Equal("150", item["LatencyMs"].N);
        Assert.Equal(SeverityClassifier.Info, item["Severity"].S);
        Assert.Equal("OPEN", item["Status"].S);
        Assert.Equal("INFO#OPEN", item["SeverityStatus"].S);
        Assert.Equal(command.Payload, item["Payload"].S);

        // Verify TTL is approximately 30 days from now (within 60 seconds tolerance)
        Assert.True(long.TryParse(item["ExpiresAt"].N, out var expiresAt));
        var expectedMinExpiresAt = beforeCall + (30 * 86400) - 5;
        var expectedMaxExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (30 * 86400) + 5;
        Assert.InRange(expiresAt, expectedMinExpiresAt, expectedMaxExpiresAt);
    }

    [Fact]
    public async Task ProcessAsync_WhenSeverityIsCritical_ShouldPublishToSns()
    {
        // Arrange
        var command = new IngestWebhookCommand(
            "github-deployments",
            500,
            "{\"event\":\"deployment_failed\"}",
            6500,
            "Internal Server Error");

        _severityClassifier.Classify(command.StatusCode, command.LatencyMs).Returns(SeverityClassifier.Critical);

        PublishRequest? capturedPublish = null;
        await _snsClient.PublishAsync(Arg.Do<PublishRequest>(req => capturedPublish = req), Arg.Any<CancellationToken>());

        // Act
        var result = await _sut.ProcessAsync(command);

        // Assert
        Assert.Equal(SeverityClassifier.Critical, result.Severity);
        Assert.NotNull(capturedPublish);
        Assert.Equal("arn:aws:sns:us-east-1:000000000000:WebhookCriticalAlerts", capturedPublish.TopicArn);
        Assert.Contains("[CRITICAL ALERT]", capturedPublish.Subject);
        Assert.Equal(SeverityClassifier.Critical, capturedPublish.MessageAttributes["Severity"].StringValue);
        Assert.Contains(command.EndpointId, capturedPublish.Message);
        Assert.Contains("500", capturedPublish.Message);
    }

    [Theory]
    [InlineData(SeverityClassifier.Info)]
    [InlineData(SeverityClassifier.Warning)]
    public async Task ProcessAsync_WhenSeverityIsNotCritical_ShouldNotPublishToSns(string severity)
    {
        // Arrange
        var command = new IngestWebhookCommand("stripe-billing", 200, "{\"status\":\"ok\"}", 1200);
        _severityClassifier.Classify(command.StatusCode, command.LatencyMs).Returns(severity);

        // Act
        var result = await _sut.ProcessAsync(command);

        // Assert
        Assert.Equal(severity, result.Severity);
        await _snsClient.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ShouldAlwaysNotifyAppSync()
    {
        // Arrange
        var command = new IngestWebhookCommand("stripe-billing", 200, "{\"ping\":true}", 100);
        _severityClassifier.Classify(command.StatusCode, command.LatencyMs).Returns(SeverityClassifier.Info);

        // Act
        await _sut.ProcessAsync(command);

        // Assert
        await _appSyncNotifier.Received(1).NotifyDashboardAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WhenSnsFails_ShouldCatchExceptionAndNotThrow()
    {
        // Arrange
        var command = new IngestWebhookCommand("stripe-billing", 503, "Service Unavailable", 1000, "Gateway down");
        _severityClassifier.Classify(command.StatusCode, command.LatencyMs).Returns(SeverityClassifier.Critical);

        _snsClient
            .PublishAsync(Arg.Any<PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<PublishResponse>>(_ => throw new AmazonSimpleNotificationServiceException("SNS error"));

        // Act & Assert (should not throw)
        var result = await _sut.ProcessAsync(command);
        Assert.NotNull(result);
        Assert.Equal("PROCESSED", result.Status);
    }

    [Fact]
    public async Task SimulateAsync_ShouldPrefixEventIdWithSim_AndPersistToDynamoDb()
    {
        // Arrange
        var request = new SimulateWebhookRequest("stripe-billing", 200, "{\"simulated\":true}", 120);
        _severityClassifier.Classify(request.StatusCode, request.LatencyMs).Returns(SeverityClassifier.Info);

        PutItemRequest? capturedRequest = null;
        await _dynamoDb.PutItemAsync(Arg.Do<PutItemRequest>(req => capturedRequest = req), Arg.Any<CancellationToken>());

        // Act
        var result = await _sut.SimulateAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("sim_", result.EventId);
        Assert.Equal("SIMULATED", result.Status);
        Assert.Equal(SeverityClassifier.Info, result.Severity);

        Assert.NotNull(capturedRequest);
        Assert.Equal(result.EventId, capturedRequest.Item["Id"].S);
        Assert.Equal("ENDPOINT#stripe-billing", capturedRequest.Item["PK"].S);
    }

    [Fact]
    public async Task SimulateAsync_WhenCritical_ShouldPublishToSns()
    {
        // Arrange
        var request = new SimulateWebhookRequest("stripe-billing", 500, "{\"error\":\"crash\"}", 4500, "Simulated Timeout");
        _severityClassifier.Classify(request.StatusCode, request.LatencyMs).Returns(SeverityClassifier.Critical);

        PublishRequest? capturedPublish = null;
        await _snsClient.PublishAsync(Arg.Do<PublishRequest>(req => capturedPublish = req), Arg.Any<CancellationToken>());

        // Act
        var result = await _sut.SimulateAsync(request);

        // Assert
        Assert.Equal(SeverityClassifier.Critical, result.Severity);
        Assert.NotNull(capturedPublish);
        Assert.Contains("[SIMULATED CRITICAL ALERT]", capturedPublish.Subject);
        Assert.Equal(SeverityClassifier.Critical, capturedPublish.MessageAttributes["Severity"].StringValue);
    }
}
