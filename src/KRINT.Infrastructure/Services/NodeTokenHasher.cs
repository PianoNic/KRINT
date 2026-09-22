using System.Security.Cryptography;
using System.Text;

namespace KRINT.Infrastructure.Services
{
    /// <summary>Hashes node tokens for storage/lookup. Node tokens are high-entropy random strings,
    /// so a plain SHA-256 (no salt) is enough to avoid keeping the secret in plaintext while still
    /// allowing an exact-match lookup on connect.</summary>
    public static class NodeTokenHasher
    {
        /// <summary>Header the node agent presents its token in. A header stays out of access and
        /// reverse-proxy logs, which a query string does not.</summary>
        public const string HeaderName = "X-Node-Token";

        /// <summary>Shortest token accepted on create or from krint.yaml. The generated ones are 43
        /// characters; this only rejects something a person typed in a hurry, because an unsalted
        /// hash of a short secret is cheap to reverse from a database dump.</summary>
        public const int MinimumLength = 16;

        public static bool IsStrongEnough(string? token) =>
            !string.IsNullOrWhiteSpace(token) && token.Trim().Length >= MinimumLength;

        /// <summary>Compares two secrets without leaking where they differ.</summary>
        public static bool ConstantTimeEquals(string a, string b)
        {
            var left = Encoding.UTF8.GetBytes(a);
            var right = Encoding.UTF8.GetBytes(b);
            return CryptographicOperations.FixedTimeEquals(left, right);
        }

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
