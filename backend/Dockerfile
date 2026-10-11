# Multi-stage Dockerfile for .NET 10 WebhookMonitor API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY ["backend/src/WebhookMonitor/WebhookMonitor.csproj", "backend/src/WebhookMonitor/"]
RUN dotnet restore "backend/src/WebhookMonitor/WebhookMonitor.csproj"

# Copy source code and build release binary
COPY backend/ backend/
WORKDIR "/src/backend/src/WebhookMonitor"
RUN dotnet publish "WebhookMonitor.csproj" -c Release -o /app/publish \
    /p:UseAppHost=false

# Runtime stage using lightweight ASP.NET 10 image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Run as non-root user for security compliance
USER $APP_UID

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "WebhookMonitor.dll"]
