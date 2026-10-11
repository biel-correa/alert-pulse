namespace WebhookMonitor.Configuration;

public class AwsOptions
{
    public const string SectionName = "AWS";

    public string Region { get; set; } = "us-east-1";
    public string? ServiceURL { get; set; }
    public bool UseLocalStack { get; set; } = false;
    public string DynamoDbTable { get; set; } = "MonitoringHub";
    public string SnsAlertTopicArn { get; set; } = "arn:aws:sns:us-east-1:000000000000:WebhookCriticalAlerts";
}
