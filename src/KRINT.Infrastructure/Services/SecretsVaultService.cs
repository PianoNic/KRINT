using System.Security.Cryptography;
using System.Text;
using KRINT.Domain;
using KRINT.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KRINT.Infrastructure.Services
{
    public class SecretsVaultService(KrintDbContext db, IConfiguration configuration) : ISecretsVaultService
    {
        private readonly byte[] _masterKey = LoadMasterKey(configuration);

        public async Task StoreAsync(string name, string plaintext, CancellationToken cancellationToken = default)
        {
            var (ciphertext, nonce, tag) = Encrypt(plaintext);

            var existing = await db.Secrets.SingleOrDefaultAsync(s => s.Name == name, cancellationToken);
            if (existing is null)
            {
                db.Secrets.Add(new Secret
                {
                    Name = name,
                    Ciphertext = ciphertext,
                    Nonce = nonce,
                    Tag = tag,
                });
            }
            else
            {
                existing.Ciphertext = ciphertext;
                existing.Nonce = nonce;
                existing.Tag = tag;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        public async Task<string?> RetrieveAsync(string name, CancellationToken cancellationToken = default)
        {
            var secret = await db.Secrets.SingleOrDefaultAsync(s => s.Name == name, cancellationToken);
            if (secret is null)
            {
                return null;
            }

            return Decrypt(secret.Ciphertext, secret.Nonce, secret.Tag);
        }

        public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
        {
            var secret = await db.Secrets.SingleOrDefaultAsync(s => s.Name == name, cancellationToken);
            if (secret is null)
            {
                return false;
            }

            db.Secrets.Remove(secret);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        private (byte[] ciphertext, byte[] nonce, byte[] tag) Encrypt(string plaintext)
        {
            var plainBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = new byte[plainBytes.Length];
            var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
            var tag = new byte[AesGcm.TagByteSizes.MaxSize];

            using var gcm = new AesGcm(_masterKey, AesGcm.TagByteSizes.MaxSize);
            gcm.Encrypt(nonce, plainBytes, ciphertext, tag);

            return (ciphertext, nonce, tag);
        }

        private string Decrypt(byte[] ciphertext, byte[] nonce, byte[] tag)
        {
            var plainBytes = new byte[ciphertext.Length];

            using var gcm = new AesGcm(_masterKey, AesGcm.TagByteSizes.MaxSize);
            gcm.Decrypt(nonce, ciphertext, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }

        private static byte[] LoadMasterKey(IConfiguration configuration)
        {
            var encoded = configuration["Vault:MasterKey"]
                ?? throw new InvalidOperationException("Vault:MasterKey is not configured.");

            byte[] key;
            try
            {
                key = Convert.FromBase64String(encoded);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Vault:MasterKey must be a base64-encoded value.", ex);
            }

            if (key.Length != 32)
            {
                throw new InvalidOperationException("Vault:MasterKey must decode to 32 bytes (256-bit AES key).");
            }

            return key;
        }
    }
}
