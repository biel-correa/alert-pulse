using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebhookMonitor.Configuration;

namespace WebhookMonitor.Services;

public interface IAppSyncNotifier
{
    Task NotifyDashboardAsync(object eventInput, CancellationToken ct = default);
    Task NotifyEventStatusUpdatedAsync(string id, string endpointId, string status, CancellationToken ct = default);
}

public class HttpAppSyncNotifier(HttpClient httpClient, IOptions<AppSyncOptions> options, ILogger<HttpAppSyncNotifier> logger) : IAppSyncNotifier
{
    private readonly AppSyncOptions _options = options.Value;

    public async Task NotifyDashboardAsync(object eventInput, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.GraphQLEndpoint))
        {
            logger.LogDebug("AppSync GraphQL endpoint is not configured. Skipping real-time notification.");
            return;
        }

        const string mutation = @"
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

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, _options.GraphQLEndpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                request.Headers.Add("x-api-key", _options.ApiKey);
            }

            var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AppSync responded with non-success status code {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to deliver real-time mutation to AppSync endpoint {Endpoint}", _options.GraphQLEndpoint);
        }
    }

    public async Task NotifyEventStatusUpdatedAsync(string id, string endpointId, string status, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.GraphQLEndpoint))
        {
            logger.LogDebug("AppSync GraphQL endpoint is not configured. Skipping status update notification.");
            return;
        }

        const string mutation = @"
            mutation UpdateStatus($id: ID!, $endpointId: ID!, $status: IncidentStatus!) {
                updateEventStatus(id: $id, endpointId: $endpointId, status: $status) {
                    id
                    endpointId
                    status
                }
            }";

        var payload = new
        {
            query = mutation,
            variables = new { id, endpointId, status }
        };

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, _options.GraphQLEndpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                request.Headers.Add("x-api-key", _options.ApiKey);
            }

            var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AppSync responded with non-success status code {StatusCode} for updateEventStatus", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to deliver updateEventStatus mutation to AppSync endpoint {Endpoint}", _options.GraphQLEndpoint);
        }
    }
}
