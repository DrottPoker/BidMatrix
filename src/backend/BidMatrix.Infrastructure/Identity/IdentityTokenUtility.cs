using System.Security.Cryptography;
using System.Text;

namespace BidMatrix.Infrastructure.Identity;

internal static class IdentityTokenUtility
{
    public static string Generate() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public static bool TryHash(string? token, out string tokenHash)
    {
        var normalized = token?.Trim() ?? string.Empty;
        if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
        {
            tokenHash = string.Empty;
            return false;
        }

        tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(normalized.ToLowerInvariant())));
        return true;
    }
}
