using WebhookMonitor.Services;
using Xunit;

namespace WebhookMonitor.Tests;

public class SeverityClassifierTests
{
    private readonly SeverityClassifier _sut = new();

    [Theory]
    [InlineData(200, 150, SeverityClassifier.Info)]
    [InlineData(201, 1999, SeverityClassifier.Info)]
    [InlineData(204, 2000, SeverityClassifier.Info)]
    [InlineData(200, 2001, SeverityClassifier.Warning)]
    [InlineData(200, 5000, SeverityClassifier.Warning)]
    [InlineData(200, 5001, SeverityClassifier.Critical)]
    [InlineData(400, 50, SeverityClassifier.Warning)]
    [InlineData(401, 1500, SeverityClassifier.Warning)]
    [InlineData(404, 2000, SeverityClassifier.Warning)]
    [InlineData(422, 4999, SeverityClassifier.Warning)]
    [InlineData(400, 5001, SeverityClassifier.Critical)]
    [InlineData(500, 10, SeverityClassifier.Critical)]
    [InlineData(502, 1000, SeverityClassifier.Critical)]
    [InlineData(503, 6000, SeverityClassifier.Critical)]
    [InlineData(504, 15000, SeverityClassifier.Critical)]
    public void Classify_Scenarios_ReturnsExpectedSeverity(int statusCode, int latencyMs, string expectedSeverity)
    {
        var result = _sut.Classify(statusCode, latencyMs);

        Assert.Equal(expectedSeverity, result);
    }
}
