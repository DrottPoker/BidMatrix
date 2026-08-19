using System.Security.Cryptography;
using System.Text;

namespace BidMatrix.Infrastructure.Identity;

public static class FederatedSubjectUtility
{
    public static string Hash(string issuer, string subject) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{issuer}\n{subject}")));
}
