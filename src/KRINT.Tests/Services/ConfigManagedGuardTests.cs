using KRINT.Application;
using KRINT.Domain;
using KRINT.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KRINT.Tests.Services
{
    public class ConfigManagedGuardTests
    {
        [Test]
        public async Task EnsureMutable_NonConfigManaged_DoesNotThrow()
        {
            var guard = new ConfigManagedGuard();
            var instance = MakeInstance(isConfigManaged: false);

            guard.EnsureMutable(instance); // no throw
            await Assert.That(guard.Bypass).IsFalse();
        }

        [Test]
        public async Task EnsureMutable_ConfigManagedWithoutBypass_Throws()
        {
            var guard = new ConfigManagedGuard();
            var instance = MakeInstance(isConfigManaged: true);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => guard.EnsureMutable(instance)));
            await Assert.That(ex!.Message).Contains("instances.yaml");
        }

        [Test]
        public async Task EnsureMutable_ConfigManagedWithBypass_DoesNotThrow()
        {
            var guard = new ConfigManagedGuard { Bypass = true };
            var instance = MakeInstance(isConfigManaged: true);

            guard.EnsureMutable(instance); // no throw
            await Assert.That(guard.Bypass).IsTrue();
        }

        [Test]
        public async Task EnsureMutableAsync_BypassFlag_SkipsDbRead()
        {
            // Use a disposed (never created) context to prove no DB read happens when Bypass=true.
            // If the guard tried to read, EF would throw.
            var guard = new ConfigManagedGuard { Bypass = true };
            await using var ctx = NewDbContext();

            await guard.EnsureMutableAsync(ctx, Guid.NewGuid(), CancellationToken.None);
        }

        [Test]
        public async Task EnsureMutableAsync_ConfigManagedRow_Throws()
        {
            var guard = new ConfigManagedGuard();
            await using var ctx = NewDbContext();
            var instance = MakeInstance(isConfigManaged: true);
            ctx.DatabaseInstances.Add(instance);
            await ctx.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await guard.EnsureMutableAsync(ctx, instance.Id, CancellationToken.None));
            await Assert.That(ex!.Message).Contains("instances.yaml");
        }

        [Test]
        public async Task EnsureMutableAsync_UnmanagedRow_DoesNotThrow()
        {
            var guard = new ConfigManagedGuard();
            await using var ctx = NewDbContext();
            var instance = MakeInstance(isConfigManaged: false);
            ctx.DatabaseInstances.Add(instance);
            await ctx.SaveChangesAsync();

            await guard.EnsureMutableAsync(ctx, instance.Id, CancellationToken.None);
        }

        private static KrintDbContext NewDbContext()
        {
            var options = new DbContextOptionsBuilder<KrintDbContext>()
                .UseInMemoryDatabase("guard-" + Guid.NewGuid())
                .Options;
            return new KrintDbContext(options);
        }

        private static DatabaseInstance MakeInstance(bool isConfigManaged)
        {
            return new DatabaseInstance
            {
                Id = Guid.NewGuid(),
                Engine = "postgres",
                Version = "18",
                DisplayName = "test",
                ContainerName = "krint-test",
                ContainerId = "deadbeef",
                Host = "localhost",
                Port = 30001,
                Username = "postgres",
                DatabaseName = "postgres",
                IsConfigManaged = isConfigManaged,
            };
        }
    }
}
