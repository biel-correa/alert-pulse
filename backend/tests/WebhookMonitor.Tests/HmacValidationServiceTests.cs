using System.Security.Cryptography;
using System.Text;
using WebhookMonitor.Services;
using Xunit;

namespace WebhookMonitor.Tests;

public class HmacValidationServiceTests
{
    private readonly HmacValidationService _sut = new();
    private const string Secret = "whsec_test_secret_123";

    [Fact]
    public void IsValidSignature_WithValidSignature_ReturnsTrue()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"payment.success\",\"amount\":100}");
        var signature = ComputeValidHeader(body, Secret);

        var result = _sut.IsValidSignature(body, signature, Secret);

        Assert.True(result);
    }

    [Fact]
    public void IsValidSignature_WithTamperedBody_ReturnsFalse()
    {
        var originalBody = Encoding.UTF8.GetBytes("{\"amount\":100}");
        var tamperedBody = Encoding.UTF8.GetBytes("{\"amount\":99999}");
        var signature = ComputeValidHeader(originalBody, Secret);

        var result = _sut.IsValidSignature(tamperedBody, signature, Secret);

        Assert.False(result);
    }

    [Fact]
    public void IsValidSignature_WithWrongSecret_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"ping\"}");
        var signature = ComputeValidHeader(body, Secret);

        var result = _sut.IsValidSignature(body, signature, "different_secret_key");

        Assert.False(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid_header_without_prefix")]
    [InlineData("sha1=d25b1842e030e463a8a3a93f1f7d2be92bf4c478a2f45f94d1b32d204d80582d")]
    [InlineData("sha256=too_short_hex")]
    [InlineData("sha256=invalid_non_hex_characters_zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void IsValidSignature_WithInvalidHeaderFormat_ReturnsFalse(string? invalidHeader)
    {
        var body = Encoding.UTF8.GetBytes("{\"test\":true}");

        var result = _sut.IsValidSignature(body, invalidHeader, Secret);

        Assert.False(result);
    }

    [Fact]
    public void IsValidSignature_WithUppercaseHex_ReturnsTrue()
    {
        var body = Encoding.UTF8.GetBytes("{\"case\":\"test\"}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hash = Convert.ToHexString(hmac.ComputeHash(body)).ToUpperInvariant();
        var signature = $"sha256={hash}";

        var result = _sut.IsValidSignature(body, signature, Secret);

        Assert.True(result);
    }

    private static string ComputeValidHeader(byte[] body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
        return $"sha256={hash}";
    }
}
