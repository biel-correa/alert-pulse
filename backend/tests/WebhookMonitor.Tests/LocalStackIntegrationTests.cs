using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebhookMonitor.Configuration;
using WebhookMonitor.Models;
using WebhookMonitor.Services;
using Xunit;

namespace WebhookMonitor.Tests;

public class LocalStackIntegrationTests
{
    private const string LocalStackUrl = "http://localhost:4566";
    private readonly bool _isLocalStackAvailable;
    private readonly AmazonDynamoDBClient _dynamoDb;
    private readonly AmazonSimpleNotificationServiceClient _snsClient;
    private readonly IOptions<AwsOptions> _awsOptions;

    public LocalStackIntegrationTests()
    {
        _dynamoDb = new AmazonDynamoDBClient("test", "test", new AmazonDynamoDBConfig
        {
            ServiceURL = LocalStackUrl,
            AuthenticationRegion = "us-east-1"
        });

        _snsClient = new AmazonSimpleNotificationServiceClient("test", "test", new AmazonSimpleNotificationServiceConfig
        {
            ServiceURL = LocalStackUrl,
            AuthenticationRegion = "us-east-1"
        });

        _awsOptions = Options.Create(new AwsOptions
        {
            DynamoDbTable = "MonitoringHub",
            SnsAlertTopicArn = "arn:aws:sns:us-east-1:000000000000:WebhookCriticalAlerts",
            ServiceURL = LocalStackUrl,
            UseLocalStack = true
        });

        try
        {
            var res = _dynamoDb.ListTablesAsync().GetAwaiter().GetResult();
            _isLocalStackAvailable = res.TableNames.Contains("MonitoringHub");
        }
        catch
        {
            _isLocalStackAvailable = false;
        }
    }

    [Fact]
    public async Task EndToEnd_LocalStack_IngestionAndAcknowledgementPipeline()
    {
        if (!_isLocalStackAvailable)
        {
            // Skip if LocalStack container is not running
            return;
        }

        // 1. Test Endpoint Repository with real LocalStack DynamoDB
        var endpointRepo = new EndpointRepository(_dynamoDb, _awsOptions);
        var customEndpointId = $"int-test-{Guid.NewGuid():N}";
        
        var createResponse = await endpointRepo.CreateAsync(new CreateEndpointRequest(
            customEndpointId,
            "Integration Test Endpoint"));

        Assert.NotNull(createResponse);
        Assert.Equal(customEndpointId, createResponse.Id);
        Assert.StartsWith("whsec_", createResponse.SecretKey);

        var retrievedConfig = await endpointRepo.GetEndpointAsync(customEndpointId);
        Assert.NotNull(retrievedConfig);
        Assert.Equal(createResponse.SecretKey, retrievedConfig.SecretKey);

        // 2. Ingest a Critical Webhook Event via WebhookIngestionService into DynamoDB and SNS
        var mockClassifier = Substitute.For<ISeverityClassifier>();
        mockClassifier.Classify(500, 4200).Returns(SeverityClassifier.Critical);

        var mockNotifier = Substitute.For<IAppSyncNotifier>();

        var ingestionService = new WebhookIngestionService(
            _dynamoDb,
            _snsClient,
            mockClassifier,
            mockNotifier,
            _awsOptions,
            NullLogger<WebhookIngestionService>.Instance);

        var payload = "{\"event\":\"order.failed\",\"error\":\"db_timeout\"}";
        var ingestCommand = new IngestWebhookCommand(
            customEndpointId,
            500,
            payload,
            4200,
            "Database Timeout");

        var ingestResponse = await ingestionService.ProcessAsync(ingestCommand);

        Assert.NotNull(ingestResponse);
        Assert.Equal("PROCESSED", ingestResponse.Status);
        Assert.Equal(SeverityClassifier.Critical, ingestResponse.Severity);

        // 3. Verify Item in DynamoDB directly via AWS SDK Query
        var queryResponse = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = "MonitoringHub",
            KeyConditionExpression = "PK = :pk AND begins_with(SK, :skPrefix)",
            FilterExpression = "Id = :id",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"ENDPOINT#{customEndpointId}"),
                [":skPrefix"] = new("EVENT#"),
                [":id"] = new(ingestResponse.EventId)
            }
        });

        Assert.Single(queryResponse.Items);
        var storedItem = queryResponse.Items[0];
        Assert.Equal(ingestResponse.EventId, storedItem["Id"].S);
        Assert.Equal("CRITICAL#OPEN", storedItem["SeverityStatus"].S);
        Assert.Equal("OPEN", storedItem["Status"].S);
        Assert.Equal(payload, storedItem["Payload"].S);

        // 4. Test Incident Acknowledgment against real LocalStack DynamoDB
        var incidentService = new IncidentService(
            _dynamoDb,
            mockNotifier,
            new HttpClient(),
            _awsOptions,
            NullLogger<IncidentService>.Instance);

        var ackResponse = await incidentService.AcknowledgeAsync(customEndpointId, ingestResponse.EventId);

        Assert.NotNull(ackResponse);
        Assert.Equal("ACKNOWLEDGED", ackResponse.Status);
        Assert.Equal(ingestResponse.EventId, ackResponse.EventId);

        // 5. Verify Item updated in DynamoDB
        var verifyUpdatedResponse = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = "MonitoringHub",
            KeyConditionExpression = "PK = :pk AND begins_with(SK, :skPrefix)",
            FilterExpression = "Id = :id",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"ENDPOINT#{customEndpointId}"),
                [":skPrefix"] = new("EVENT#"),
                [":id"] = new(ingestResponse.EventId)
            }
        });

        Assert.Single(verifyUpdatedResponse.Items);
        var updatedItem = verifyUpdatedResponse.Items[0];
        Assert.Equal("ACKNOWLEDGED", updatedItem["Status"].S);
        Assert.Equal("CRITICAL#ACKNOWLEDGED", updatedItem["SeverityStatus"].S);
    }
}
