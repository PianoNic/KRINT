using System.Security.Cryptography;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KRINT.Tests.Services
{
    public class SecretsVaultServiceTests
    {
        private static (SecretsVaultService vault, KrintDbContext db) CreateVault()
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

            return (new SecretsVaultService(db, configuration), db);
        }

        [Test]
        public async Task RetrieveAsync_AfterStore_ReturnsOriginalValue()
        {
            var (vault, _) = CreateVault();

            await vault.StoreAsync("db.prod", "p4ssw0rd!!");
            var result = await vault.RetrieveAsync("db.prod");

            await Assert.That(result).IsEqualTo("p4ssw0rd!!");
        }

        [Test]
        public async Task StoreAsync_SameNameTwice_OverwritesExistingValue()
        {
            var (vault, db) = CreateVault();

            await vault.StoreAsync("k", "first");
            await vault.StoreAsync("k", "second");

            var result = await vault.RetrieveAsync("k");

            await Assert.That(result).IsEqualTo("second");
            await Assert.That(await db.Secrets.CountAsync()).IsEqualTo(1);
        }

        [Test]
        public async Task RetrieveAsync_MissingName_ReturnsNull()
        {
            var (vault, _) = CreateVault();

            var result = await vault.RetrieveAsync("nope");

            await Assert.That(result).IsNull();
        }

        [Test]
        public async Task DeleteAsync_ExistingName_ReturnsTrueAndRemovesRow()
        {
            var (vault, db) = CreateVault();

            await vault.StoreAsync("k", "v");
            var deleted = await vault.DeleteAsync("k");

            await Assert.That(deleted).IsTrue();
            await Assert.That(await db.Secrets.CountAsync()).IsEqualTo(0);
        }

        [Test]
        public async Task DeleteAsync_MissingName_ReturnsFalse()
        {
            var (vault, _) = CreateVault();

            var deleted = await vault.DeleteAsync("nope");

            await Assert.That(deleted).IsFalse();
        }

        [Test]
        public async Task RetrieveAsync_TamperedCiphertext_ThrowsCryptographicException()
        {
            var (vault, db) = CreateVault();

            await vault.StoreAsync("k", "secret");
            var row = await db.Secrets.SingleAsync();
            row.Ciphertext[0] ^= 0xFF;
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<CryptographicException>(async () => await vault.RetrieveAsync("k"));
        }

        [Test]
        public async Task RetrieveAsync_TamperedTag_ThrowsCryptographicException()
        {
            var (vault, db) = CreateVault();

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
