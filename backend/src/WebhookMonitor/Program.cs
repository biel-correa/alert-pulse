using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Serilog;
using WebhookMonitor.Configuration;
using WebhookMonitor.Endpoints;
using WebhookMonitor.Extensions;
using WebhookMonitor.Middleware;

var builder = WebApplication.CreateBuilder(args);

// 1. Serilog Setup
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"));

    // 2. AWS SDK & Configuration
    builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
    builder.Services.AddAlertPulseAws(builder.Configuration);
    builder.Services.Configure<AppSyncOptions>(builder.Configuration.GetSection(AppSyncOptions.SectionName));
    builder.Services.AddAlertPulseServices();

    // 3. Health Checks, CORS & Rate Limiting
    builder.Services.AddAlertPulseHealthChecks();
    builder.Services.AddAlertPulseCors();
    builder.Services.AddAlertPulseRateLimiting(builder.Configuration);

    // 4. Swagger / OpenAPI Documentation
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Alert Pulse - Webhook Dispatcher & Incident Monitor API",
            Version = "v1",
            Description = "High-throughput webhook ingestion, HMAC validation, and incident monitoring hub."
        });
    });

    var app = builder.Build();

    // 5. Middleware Pipeline
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseSerilogRequestLogging();

    app.UseCors("AllowFrontend");
    app.UseRateLimiter();

    if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", true))
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Alert Pulse API v1");
            c.RoutePrefix = "swagger";
        });
    }

    // 6. Health Check Endpoints
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live")
    }).WithTags("Health");

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    }).WithTags("Health");

    // 7. Root Information Endpoint
    app.MapGet("/", () => Results.Ok(new
    {
        Service = "Alert Pulse - Webhook Dispatcher & Incident Monitor",
        Status = "Online",
        Timestamp = DateTimeOffset.UtcNow,
        Docs = "/swagger"
    })).ExcludeFromDescription();

    // 8. Ingestion & Operational Endpoints
    app.MapIngestionEndpoints();
    app.MapOperationalEndpoints();

    app.Run();

public partial class Program { }


