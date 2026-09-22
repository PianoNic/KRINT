using System.Security.Cryptography;
using System.Text;
using KRINT.Domain;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KRINT.Tests.Services
{
    public class SecretsVaultServiceTests
    {
        private static (SecretsVaultService vault, KrintDbContext db, byte[] key) CreateVault()
        {
            var options = new DbContextOptionsBuilder<KrintDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var db = new KrintDbContext(options);

            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Vault:MasterKey"] = Convert.ToBase64String(key),
                })
                .Build();

            return (new SecretsVaultService(db, configuration), db, key);
        }

        [Test]
        public async Task RetrieveAsync_AfterStore_ReturnsOriginalValue()
        {
            var (vault, _, _) = CreateVault();

            await vault.StoreAsync("db.prod", "p4ssw0rd!!");
            var result = await vault.RetrieveAsync("db.prod");

            await Assert.That(result).IsEqualTo("p4ssw0rd!!");
        }

        [Test]
        public async Task StoreAsync_SameNameTwice_OverwritesExistingValue()
        {
            var (vault, db, _) = CreateVault();

            await vault.StoreAsync("k", "first");
            await vault.StoreAsync("k", "second");

            var result = await vault.RetrieveAsync("k");

            await Assert.That(result).IsEqualTo("second");
            await Assert.That(await db.Secrets.CountAsync()).IsEqualTo(1);
        }

        [Test]
        public async Task RetrieveAsync_RowMovedUnderAnotherName_Fails()
        {
            var (vault, db, _) = CreateVault();
            await vault.StoreAsync("db.a", "secret-a");
            var row = await db.Secrets.SingleAsync(s => s.Name == "db.a");
            db.Secrets.Add(new Secret { Name = "db.b", Ciphertext = row.Ciphertext, Nonce = row.Nonce, Tag = row.Tag });
            await db.SaveChangesAsync();

            await Assert.That(async () => await vault.RetrieveAsync("db.b")).Throws<CryptographicException>();
        }

        [Test]
        public async Task RetrieveAsync_LegacyUnboundRow_ReadsAndRebinds()
        {
            var (vault, db, key) = CreateVault();
            // A row written by the previous vault: same key, no associated data.
            var plain = Encoding.UTF8.GetBytes("legacy-secret");
            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[plain.Length];
            var tag = new byte[16];
            using (var gcm = new AesGcm(key, 16)) gcm.Encrypt(nonce, plain, cipher, tag);
            db.Secrets.Add(new Secret { Name = "db.legacy", Ciphertext = cipher, Nonce = nonce, Tag = tag });
            await db.SaveChangesAsync();

            await Assert.That(await vault.RetrieveAsync("db.legacy")).IsEqualTo("legacy-secret");

            // Second read goes through the bound path: the row was rewritten with the name as AAD.
            var rebound = await db.Secrets.SingleAsync(s => s.Name == "db.legacy");
            await Assert.That(rebound.Nonce.SequenceEqual(nonce)).IsFalse();
            await Assert.That(await vault.RetrieveAsync("db.legacy")).IsEqualTo("legacy-secret");
        }

        [Test]
        public async Task RetrieveAsync_MissingName_ReturnsNull()
        {
            var (vault, _, _) = CreateVault();

            var result = await vault.RetrieveAsync("nope");

            await Assert.That(result).IsNull();
        }

        [Test]
        public async Task DeleteAsync_ExistingName_ReturnsTrueAndRemovesRow()
        {
            var (vault, db, _) = CreateVault();

            await vault.StoreAsync("k", "v");
            var deleted = await vault.DeleteAsync("k");

            await Assert.That(deleted).IsTrue();
            await Assert.That(await db.Secrets.CountAsync()).IsEqualTo(0);
        }

        [Test]
        public async Task DeleteAsync_MissingName_ReturnsFalse()
        {
            var (vault, _, _) = CreateVault();

            var deleted = await vault.DeleteAsync("nope");

            await Assert.That(deleted).IsFalse();
        }

        [Test]
        public async Task RetrieveAsync_TamperedCiphertext_ThrowsCryptographicException()
        {
            var (vault, db, _) = CreateVault();

            await vault.StoreAsync("k", "secret");
            var row = await db.Secrets.SingleAsync();
            row.Ciphertext[0] ^= 0xFF;
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<CryptographicException>(async () => await vault.RetrieveAsync("k"));
        }

        [Test]
        public async Task RetrieveAsync_TamperedTag_ThrowsCryptographicException()
        {
            var (vault, db, _) = CreateVault();

            await vault.StoreAsync("k", "secret");
            var row = await db.Secrets.SingleAsync();
            row.Tag[0] ^= 0xFF;
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<CryptographicException>(async () => await vault.RetrieveAsync("k"));
        }

        [Test]
        public void Constructor_MissingMasterKey_ThrowsInvalidOperationException()
        {
            var options = new DbContextOptionsBuilder<KrintDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new KrintDbContext(options);
            var configuration = new ConfigurationBuilder().Build();

            Assert.Throws<InvalidOperationException>(() => new SecretsVaultService(db, configuration));
        }

        [Test]
        public void Constructor_MasterKeyNotBase64_ThrowsInvalidOperationException()
        {
            var options = new DbContextOptionsBuilder<KrintDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new KrintDbContext(options);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Vault:MasterKey"] = "not-base64-!!!",
                })
                .Build();

            Assert.Throws<InvalidOperationException>(() => new SecretsVaultService(db, configuration));
        }

        [Test]
        public void Constructor_MasterKeyWrongLength_ThrowsInvalidOperationException()
        {
            var options = new DbContextOptionsBuilder<KrintDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new KrintDbContext(options);
            var shortKey = Convert.ToBase64String(new byte[16]);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Vault:MasterKey"] = shortKey,
                })
                .Build();

            Assert.Throws<InvalidOperationException>(() => new SecretsVaultService(db, configuration));
        }
    }
}
