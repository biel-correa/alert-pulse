namespace WebhookMonitor.Services;

public interface ISeverityClassifier
{
    string Classify(int statusCode, int latencyMs);
}

public class SeverityClassifier : ISeverityClassifier
{
    public const string Critical = "CRITICAL";
    public const string Warning = "WARNING";
    public const string Info = "INFO";

    public string Classify(int statusCode, int latencyMs)
    {
        // Intolerable latency or 5xx server errors trigger CRITICAL
        if (statusCode >= 500 || latencyMs > 5000)
        {
            return Critical;
        }

        // Degraded latency or 4xx client errors trigger WARNING
        if (statusCode >= 400 || latencyMs > 2000)
        {
            return Warning;
        }

        return Info;
    }
}
