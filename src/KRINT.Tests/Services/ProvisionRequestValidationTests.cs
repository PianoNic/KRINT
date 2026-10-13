using KRINT.Application;
using KRINT.Application.Command.Provision;
using KRINT.Application.Dtos.Provision;

namespace KRINT.Tests.Services
{
    /// <summary>Everything ProvisionDatabaseCommand can reject must be rejected before a
    /// container exists, so a bad request never leaves an orphaned instance.</summary>
    public class ProvisionRequestValidationTests
    {
        private static ProvisionRequestDto Request(
            string engine = "postgres",
            string displayName = "app-db",
            string? defaultDb = null,
            string[]? databases = null,
            ProvisionUserSpec[]? users = null) => new()
        {
            Engine = engine,
            Version = "18",
            DisplayName = displayName,
            DefaultDatabaseName = defaultDb,
            Databases = databases ?? [],
            Users = users ?? [],
        };

        [Test]
        [Arguments("")]
        [Arguments("   ")]
        public async Task Empty_display_name_is_rejected(string name)
        {
            await Assert.That(() => ProvisionDatabaseCommandHandler.ValidateRequest(Request(displayName: name))).Throws<ArgumentException>();
        }

        [Test]
        public async Task Display_name_over_64_chars_is_rejected()
        {
            await Assert.That(() => ProvisionDatabaseCommandHandler.ValidateRequest(Request(displayName: new string('a', 65)))).Throws<ArgumentException>();
        }

        [Test]
        public async Task Display_name_is_trimmed()
        {
            await Assert.That(ProvisionDatabaseCommandHandler.ValidateRequest(Request(displayName: "  app-db  "))).IsEqualTo("app-db");
        }

        [Test]
        public async Task Grant_to_engine_default_database_is_accepted()
        {
            var req = Request(users: [new ProvisionUserSpec { Name = "alice", GrantDatabases = ["postgres"] }]);
            await Assert.That(() => ProvisionDatabaseCommandHandler.ValidateRequest(req)).ThrowsNothing();
        }

        [Test]
        public async Task Grant_to_unknown_database_is_rejected_before_provisioning()
        {
            var req = Request(databases: ["analytics"], users: [new ProvisionUserSpec { Name = "alice", GrantDatabases = ["reporting"] }]);
            await Assert.That(() => ProvisionDatabaseCommandHandler.ValidateRequest(req)).Throws<ArgumentException>();
        }

        [Test]
        public async Task Invalid_extra_database_name_is_rejected()
        {
            await Assert.That(() => ProvisionDatabaseCommandHandler.ValidateRequest(Request(databases: ["1bad"]))).Throws<ArgumentException>();
        }

        [Test]
        public async Task Cassandra_keyspace_with_hyphen_is_rejected()
        {
            await Assert.That(() => ProvisionDatabaseCommandHandler.ValidateRequest(Request(engine: "cassandra", defaultDb: "my-ks"))).Throws<ArgumentException>();
        }

        [Test]
        public async Task Users_on_engine_without_user_management_are_rejected()
        {
            var req = Request(engine: "redis", users: [new ProvisionUserSpec { Name = "alice", GrantDatabases = [] }]);
            await Assert.That(() => ProvisionDatabaseCommandHandler.ValidateRequest(req)).Throws<ArgumentException>();
        }

        [Test]
        public async Task Unsupported_engine_is_rejected()
        {
            await Assert.That(() => ProvisionDatabaseCommandHandler.ValidateRequest(Request(engine: "oracle"))).Throws<ArgumentException>();
        }

        [Test]
        public async Task DisplayNameValidator_rejects_null()
        {
            await Assert.That(() => DisplayNameValidator.Normalize(null)).Throws<ArgumentException>();
        }
    }
}
