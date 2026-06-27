using System.Security.Cryptography;
using System.Text;

namespace KRINT.Infrastructure.Services
{
    /// <summary>Hashes node tokens for storage/lookup. Node tokens are high-entropy random strings,
    /// so a plain SHA-256 (no salt) is enough to avoid keeping the secret in plaintext while still
    /// allowing an exact-match lookup on connect.</summary>
    public static class NodeTokenHasher
    {
        public static string Hash(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(bytes);
        }

        /// <summary>Generates a new URL-safe random node token (32 bytes, ~43 chars).</summary>
        public static string Generate()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }
    }
}
