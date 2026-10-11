using Amazon.DynamoDBv2;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WebhookMonitor.Configuration;

namespace WebhookMonitor.Health;

public class DynamoDbHealthCheck(IAmazonDynamoDB dynamoDb, IOptions<AwsOptions> options) : IHealthCheck
{
    private readonly string _tableName = options.Value.DynamoDbTable;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await dynamoDb.DescribeTableAsync(_tableName, cancellationToken);
            var status = response.Table?.TableStatus;

            return status == TableStatus.ACTIVE || status == TableStatus.UPDATING
                ? HealthCheckResult.Healthy($"DynamoDB table '{_tableName}' is {status}.")
                : HealthCheckResult.Degraded($"DynamoDB table '{_tableName}' is in state {status}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Failed to communicate with DynamoDB table '{_tableName}'.", ex);
        }
    }
}
