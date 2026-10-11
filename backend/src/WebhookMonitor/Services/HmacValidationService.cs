using System.Security.Cryptography;
using System.Text;

namespace WebhookMonitor.Services;

public interface IHmacValidationService
{
    bool IsValidSignature(byte[] bodyBytes, string? signatureHeader, string secretKey);
}

public class HmacValidationService : IHmacValidationService
{
    public const string Sha256Prefix = "sha256=";

    public bool IsValidSignature(byte[] bodyBytes, string? signatureHeader, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) ||
            string.IsNullOrWhiteSpace(secretKey) ||
            !signatureHeader.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedHex = signatureHeader[Sha256Prefix.Length..].Trim();
        if (expectedHex.Length != 64) // SHA-256 produces 32 bytes = 64 hex characters
        {
            return false;
        }

        byte[] expectedBytes;
        try
        {
            expectedBytes = Convert.FromHexString(expectedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        using var hmac = new HMACSHA256(keyBytes);
        var computedBytes = hmac.ComputeHash(bodyBytes);

        // Constant-time comparison to prevent timing side-channel attacks
        return CryptographicOperations.FixedTimeEquals(expectedBytes, computedBytes);
    }
}
