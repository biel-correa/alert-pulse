using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.SimpleNotificationService;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using WebhookMonitor.Extensions;
using WebhookMonitor.Models;
using WebhookMonitor.Services;

namespace WebhookMonitor.Tests;

public class RateLimitingAndResilienceTests
{
    private class CustomApiFactory : WebApplicationFactory<Program>
    {
        private readonly int _permitLimit;

        public CustomApiFactory(int permitLimit = 2)
        {
            _permitLimit = permitLimit;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("RateLimiting:PermitLimit", _permitLimit.ToString());
            builder.UseSetting("RateLimiting:WindowSeconds", "5");

            builder.ConfigureTestServices(services =>
            {
                // Substitute AWS dependencies so tests don't require network calls
                var mockDynamo = Substitute.For<IAmazonDynamoDB>();
                var mockSns = Substitute.For<IAmazonSimpleNotificationService>();
                var mockRepo = Substitute.For<IEndpointRepository>();
                var mockIngestion = Substitute.For<IWebhookIngestionService>();

                mockRepo.GetEndpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(new EndpointConfig("test-endpoint", "Test", "secret-key", "/api/v1/webhooks/ingest/test-endpoint", "DEFAULT", DateTimeOffset.UtcNow, true));

                mockIngestion.SimulateAsync(Arg.Any<SimulateWebhookRequest>(), Arg.Any<CancellationToken>())
                    .Returns(new SimulateWebhookResponse("sim-123", "PROCESSED", "INFO"));

                mockIngestion.ProcessAsync(Arg.Any<IngestWebhookCommand>(), Arg.Any<CancellationToken>())
                    .Returns(new IngestWebhookResponse("ev-123", "test-endpoint", "PROCESSED", "INFO", DateTimeOffset.UtcNow));

                services.RemoveAll<IAmazonDynamoDB>();
                services.RemoveAll<IAmazonSimpleNotificationService>();
                services.RemoveAll<IEndpointRepository>();
                services.RemoveAll<IWebhookIngestionService>();

                services.AddSingleton(mockDynamo);
                services.AddSingleton(mockSns);
                services.AddSingleton(mockRepo);
                services.AddSingleton(mockIngestion);
            });
        }
    }

    [Fact]
    public async Task SimulateEndpoint_ExceedingRateLimit_Returns429TooManyRequests()
    {
        // Arrange: Factory with permit limit of 2 requests
        using var factory = new CustomApiFactory(permitLimit: 2);
        using var client = factory.CreateClient();

        var payload = new SimulateWebhookRequest(
            EndpointId: "test-ep",
            StatusCode: 200,
            LatencyMs: 50,
            Payload: "{\"test\":true}"
        );

        // Act: 1st and 2nd calls should succeed (200 OK)
        var response1 = await client.PostAsJsonAsync("/api/v1/webhooks/simulate", payload);
        var response2 = await client.PostAsJsonAsync("/api/v1/webhooks/simulate", payload);

        // 3rd call exceeds limit of 2 within window
        var response3 = await client.PostAsJsonAsync("/api/v1/webhooks/simulate", payload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, response3.StatusCode);
    }

    [Fact]
    public async Task IngestionEndpoint_ExceedingRateLimit_Returns429TooManyRequests()
    {
        // Arrange
        using var factory = new CustomApiFactory(permitLimit: 2);
        using var client = factory.CreateClient();

        var secret = "secret-key";
        var content = "{\"event\":\"ping\"}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(content)));

        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/ingest/test-endpoint")
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
        request1.Headers.Add("X-Hub-Signature-256", $"sha256={hash}");

        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/ingest/test-endpoint")
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
        request2.Headers.Add("X-Hub-Signature-256", $"sha256={hash}");

        var request3 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/ingest/test-endpoint")
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
        request3.Headers.Add("X-Hub-Signature-256", $"sha256={hash}");

        // Act
        var res1 = await client.SendAsync(request1);
        var res2 = await client.SendAsync(request2);
        var res3 = await client.SendAsync(request3);

        // Assert
        Assert.NotEqual(HttpStatusCode.TooManyRequests, res1.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, res2.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, res3.StatusCode);
    }
}
