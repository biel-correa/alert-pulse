---
title: ".NET 10 Web API Architecture"
tags:
  - backend
  - dotnet
  - csharp
  - architecture
  - clean-architecture
date: 2026-10-11
status: approved
---

# .NET 10 Web API Architecture

The backend of **Alert Pulse** is built on **.NET 10** leveraging the **Minimal APIs** pattern combined with a vertical slice organization (*Feature-based architecture*) to guarantee maximum request throughput, low memory allocations, and ultra-fast cold starts.

---

## 📁 Solution Structure

```text
src/
└── WebhookMonitor/
    ├── Configuration/
    │   ├── AwsOptions.cs              # AWS / LocalStack configuration mappings
    │   └── AppSyncOptions.cs          # AppSync GraphQL endpoint & credentials
    ├── Features/
    │   ├── Ingestion/
    │   │   ├── IngestWebhookEndpoint.cs
    │   │   ├── IngestWebhookCommand.cs
    │   │   └── WebhookIngestionService.cs
    │   ├── Simulation/
    │   │   └── SimulateWebhookEndpoint.cs
    │   ├── Incidents/
    │   │   ├── AcknowledgeIncidentEndpoint.cs
    │   │   ├── RetryIncidentEndpoint.cs
    │   │   └── IncidentManagementService.cs
    │   └── Endpoints/
    │       ├── EndpointsCrud.cs
    │       └── EndpointConfigRepository.cs
    ├── Infrastructure/
    │   ├── Security/
    │   │   └── HmacValidationService.cs
    │   ├── Notifications/
    │   │   ├── IAppSyncNotifier.cs
    │   │   └── HttpAppSyncNotifier.cs
    │   └── DynamoDb/
    │       └── DynamoDbExtensions.cs
    ├── Middleware/
    │   ├── ExceptionHandlingMiddleware.cs
    │   └── EnableBufferingMiddleware.cs
    ├── Program.cs                     # Entrypoint & Dependency Injection setup
    └── appsettings.json
```

---

## 💉 Dependency Injection & AWS Configuration

The official AWS SDK for .NET (`AWSSDK.Extensions.NETCore.Setup`) registers DynamoDB and SNS clients. In local development environments, requests are redirected to **LocalStack** via the `ServiceURL` configuration property.

### Sample `appsettings.json`
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AWS": {
    "Region": "us-east-1",
    "ServiceURL": "http://localhost:4566",
    "UseLocalStack": true,
    "DynamoDbTable": "MonitoringHub",
    "SnsAlertTopicArn": "arn:aws:sns:us-east-1:000000000000:WebhookCriticalAlerts"
  },
  "AppSync": {
    "GraphQLEndpoint": "https://localhost:4566/graphql",
    "ApiKey": "da2-test-api-key"
  }
}
```

### Setup in `Program.cs`
```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. AWS SDK Configuration
var awsOptions = builder.Configuration.GetAWSOptions();
if (builder.Configuration.GetValue<bool>("AWS:UseLocalStack"))
{
    var serviceUrl = builder.Configuration["AWS:ServiceURL"];
    awsOptions.DefaultClientConfig.ServiceURL = serviceUrl;
    awsOptions.DefaultClientConfig.AuthenticationRegion = "us-east-1";
}
builder.Services.AddDefaultAWSOptions(awsOptions);
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddAWSService<IAmazonSimpleNotificationService>();

// 2. Domain Services Registration
builder.Services.AddHttpClient<IAppSyncNotifier, HttpAppSyncNotifier>();
builder.Services.AddScoped<IHmacValidationService, HmacValidationService>();
builder.Services.AddScoped<WebhookIngestionService>();
builder.Services.AddScoped<IncidentManagementService>();

// 3. Middlewares and CORS
builder.Services.AddCors(options => {
    options.AddPolicy("AllowFrontend", policy => {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors("AllowFrontend");
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 4. Endpoint Route Mapping
app.MapIngestionEndpoints();
app.MapOperationalEndpoints();

app.Run();
```

---

## ⚡ High-Performance Practices in .NET 10

1. **System.Text.Json with Source Generators:** Zero-reflection serialization for large webhook payloads, minimizing garbage collection pauses.
2. **Selective Request Buffering:** `context.Request.EnableBuffering()` is scoped specifically to webhook ingestion endpoints to read the raw body for HMAC calculations without throttling throughput elsewhere.
3. **Decoupled Side-Effect Execution:** Primary write atomicity in DynamoDB is guaranteed, while SNS and AppSync notifications execute concurrently or without blocking the caller's `202 Accepted` response.

---

## 🔗 Related Notes
* Cryptographic signature details: [[Webhook-Ingestion|Ingestion Pipeline & HMAC Validation]].
* Complete service implementation: [[Service-Implementations|C# Service Implementations]].
* REST API contracts: [[Operational-Endpoints|REST Endpoints Catalog]].
