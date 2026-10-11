using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebhookMonitor.Configuration;
using WebhookMonitor.Services;
using Xunit;

namespace WebhookMonitor.Tests;

public class HttpAppSyncNotifierTests
{
    private class TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handlerFunc(request));
        }
    }

    [Fact]
    public async Task NotifyDashboardAsync_WhenEndpointConfigured_SendsPostWithMutationAndApiKey()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new TestHttpMessageHandler(req =>
        {
            capturedRequest = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new AppSyncOptions
        {
            GraphQLEndpoint = "https://appsync.us-east-1.amazonaws.com/graphql",
            ApiKey = "da2-testkey123"
        });

        var sut = new HttpAppSyncNotifier(httpClient, options, NullLogger<HttpAppSyncNotifier>.Instance);

        var eventInput = new
        {
            id = "evt-123",
            endpointId = "stripe-billing",
            statusCode = 200,
            latencyMs = 150,
            severity = "INFO",
            status = "OPEN"
        };

        // Act
        await sut.NotifyDashboardAsync(eventInput);

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal("https://appsync.us-east-1.amazonaws.com/graphql", capturedRequest.RequestUri?.ToString());
        Assert.True(capturedRequest.Headers.Contains("x-api-key"));
        Assert.Equal("da2-testkey123", capturedRequest.Headers.GetValues("x-api-key").First());

        Assert.NotNull(capturedBody);
        Assert.Contains("publishWebhookEvent", capturedBody);
        Assert.Contains("evt-123", capturedBody);
        Assert.Contains("stripe-billing", capturedBody);
    }

    [Fact]
    public async Task NotifyDashboardAsync_WhenEndpointNotConfigured_DoesNotSendRequest()
    {
        // Arrange
        var requestSent = false;
        var handler = new TestHttpMessageHandler(_ =>
        {
            requestSent = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new AppSyncOptions
        {
            GraphQLEndpoint = "",
            ApiKey = ""
        });

        var sut = new HttpAppSyncNotifier(httpClient, options, NullLogger<HttpAppSyncNotifier>.Instance);

        // Act
        await sut.NotifyDashboardAsync(new { id = "evt-123" });

        // Assert
        Assert.False(requestSent);
    }

    [Fact]
    public async Task NotifyDashboardAsync_WhenHttpThrowsException_DoesNotThrow()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(_ => throw new HttpRequestException("Network failure"));
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new AppSyncOptions
        {
            GraphQLEndpoint = "https://appsync.us-east-1.amazonaws.com/graphql",
            ApiKey = "da2-key"
        });

        var sut = new HttpAppSyncNotifier(httpClient, options, NullLogger<HttpAppSyncNotifier>.Instance);

        // Act & Assert (must not throw)
        await sut.NotifyDashboardAsync(new { id = "evt-123" });
    }

    [Fact]
    public async Task NotifyEventStatusUpdatedAsync_WhenEndpointConfigured_SendsUpdateStatusMutation()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new TestHttpMessageHandler(req =>
        {
            capturedRequest = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new AppSyncOptions
        {
            GraphQLEndpoint = "https://appsync.us-east-1.amazonaws.com/graphql",
            ApiKey = "da2-testkey123"
        });

        var sut = new HttpAppSyncNotifier(httpClient, options, NullLogger<HttpAppSyncNotifier>.Instance);

        // Act
        await sut.NotifyEventStatusUpdatedAsync("evt-999", "stripe-billing", "ACKNOWLEDGED");

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.NotNull(capturedBody);
        Assert.Contains("updateEventStatus", capturedBody);
        Assert.Contains("evt-999", capturedBody);
        Assert.Contains("ACKNOWLEDGED", capturedBody);
    }

    [Fact]
    public async Task NotifyEventStatusUpdatedAsync_WhenEndpointNotConfigured_DoesNotSendRequest()
    {
        // Arrange
        var requestSent = false;
        var handler = new TestHttpMessageHandler(_ =>
        {
            requestSent = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new AppSyncOptions
        {
            GraphQLEndpoint = null!,
            ApiKey = null!
        });

        var sut = new HttpAppSyncNotifier(httpClient, options, NullLogger<HttpAppSyncNotifier>.Instance);

        // Act
        await sut.NotifyEventStatusUpdatedAsync("evt-999", "stripe-billing", "ACKNOWLEDGED");

        // Assert
        Assert.False(requestSent);
    }
}
