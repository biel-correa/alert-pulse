namespace WebhookMonitor.Configuration;

public class AppSyncOptions
{
    public const string SectionName = "AppSync";

    public string GraphQLEndpoint { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public bool UseIamAuth { get; set; } = false;
}
