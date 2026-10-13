using System.Security.Cryptography;

namespace KRINT.Infrastructure.Services
{
    /// <summary>
    /// A stored secret did not decrypt under the configured Vault:MasterKey. Derives from
    /// <see cref="CryptographicException"/> so existing handlers keep working, but carries a
    /// message an operator can act on instead of "The computed authentication tag did not match".
    /// </summary>
    public sealed class VaultKeyMismatchException : CryptographicException
    {
        public VaultKeyMismatchException(string message) : base(message) { }

        public VaultKeyMismatchException(string message, Exception inner) : base(message, inner) { }
    }
}
