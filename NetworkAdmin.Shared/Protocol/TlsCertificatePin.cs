using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NetworkAdmin.Shared.Protocol;

public static class TlsCertificatePin
{
    public static bool IsValidFingerprint(string? value) => Normalize(value) is not null;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Any(c => !Uri.IsHexDigit(c) && c is not ':' and not '-' && !char.IsWhiteSpace(c))) return null;
        var normalized = new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit) ? normalized : null;
    }

    public static bool Matches(X509Certificate? certificate, string? expectedFingerprint)
    {
        var expected = Normalize(expectedFingerprint);
        if (certificate is null || expected is null) return false;
        try
        {
            var actual = SHA256.HashData(certificate.GetRawCertData());
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expected));
        }
        catch (CryptographicException) { return false; }
    }
}
