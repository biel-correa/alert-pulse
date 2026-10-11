using Amazon;
using Amazon.DynamoDBv2;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WebhookMonitor.Configuration;
using WebhookMonitor.Health;
using WebhookMonitor.Services;

namespace WebhookMonitor.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAlertPulseAws(this IServiceCollection services, IConfiguration configuration)
    {
        var awsSection = configuration.GetSection(AwsOptions.SectionName);
        services.Configure<AwsOptions>(awsSection);

        var awsOptions = awsSection.Get<AwsOptions>() ?? new AwsOptions();

        if (awsOptions.UseLocalStack && !string.IsNullOrWhiteSpace(awsOptions.ServiceURL))
        {
            var dynamoConfig = new AmazonDynamoDBConfig
            {
                ServiceURL = awsOptions.ServiceURL,
                AuthenticationRegion = awsOptions.Region
            };
            services.AddSingleton<IAmazonDynamoDB>(new AmazonDynamoDBClient("test", "test", dynamoConfig));

            var snsConfig = new AmazonSimpleNotificationServiceConfig
            {
                ServiceURL = awsOptions.ServiceURL,
                AuthenticationRegion = awsOptions.Region
            };
            services.AddSingleton<IAmazonSimpleNotificationService>(new AmazonSimpleNotificationServiceClient("test", "test", snsConfig));
        }
        else
        {
            var defaultAwsOptions = configuration.GetAWSOptions();
            if (!string.IsNullOrWhiteSpace(awsOptions.Region))
            {
                defaultAwsOptions.Region = RegionEndpoint.GetBySystemName(awsOptions.Region);
            }

            services.AddDefaultAWSOptions(defaultAwsOptions);
            services.AddAWSService<IAmazonDynamoDB>();
            services.AddAWSService<IAmazonSimpleNotificationService>();
        }

        return services;
    }

    public static IServiceCollection AddAlertPulseServices(this IServiceCollection services)
    {
        services.AddSingleton<IHmacValidationService, HmacValidationService>();
        services.AddSingleton<ISeverityClassifier, SeverityClassifier>();
        services.AddScoped<IEndpointRepository, EndpointRepository>();
        services.AddScoped<IWebhookIngestionService, WebhookIngestionService>();
        services.AddHttpClient<IIncidentService, IncidentService>()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
            });

        services.AddHttpClient<IAppSyncNotifier, HttpAppSyncNotifier>()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
            });

        return services;
    }

    public static IServiceCollection AddAlertPulseHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("API process is running."), tags: ["live"])
            .AddCheck<DynamoDbHealthCheck>("dynamodb", tags: ["ready"]);

        return services;
    }

    public static IServiceCollection AddAlertPulseCors(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins("http://localhost:4200")
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .WithExposedHeaders("X-Correlation-Id");
            });
        });

        return services;
    }

    public static IServiceCollection AddAlertPulseRateLimiting(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var permitLimit = configuration?.GetValue<int>("RateLimiting:PermitLimit") ?? 100;
        if (permitLimit <= 0) permitLimit = 100;

        var windowSeconds = configuration?.GetValue<int>("RateLimiting:WindowSeconds") ?? 10;
        if (windowSeconds <= 0) windowSeconds = 10;
        var windowDuration = TimeSpan.FromSeconds(windowSeconds);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                if (context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "Too many requests. Ingestion rate limit exceeded. Please retry later.",
                    statusCode = StatusCodes.Status429TooManyRequests
                }, cancellationToken: token);
            };

            options.AddPolicy("IngestionRateLimit", httpContext =>
            {
                // Partition by client IP or endpointId if available
                var endpointId = httpContext.Request.RouteValues["endpointId"]?.ToString();
                var partitionKey = !string.IsNullOrWhiteSpace(endpointId)
                    ? $"endpoint:{endpointId}"
                    : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

                return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = windowDuration,
                        QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });
        });

        return services;
    }
}
