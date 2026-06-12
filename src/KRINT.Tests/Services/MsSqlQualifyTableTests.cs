using KRINT.Infrastructure.Services;

namespace KRINT.Tests.Services
{
    /// <summary>
    /// Non-dbo MSSQL tables are browsed as "schema.name"; the unqualified form only resolves
    /// in the user's default schema. Mirrors the Postgres rule (issue #136).
    /// </summary>
    public class MsSqlQualifyTableTests
    {
        [Test]
        public async Task BareName_QualifiesAsDbo()
        {
            var sql = MsSqlInnerSchemaService.QualifyTable("EventContent");
            await Assert.That(sql).IsEqualTo("[dbo].[EventContent]");
        }

        [Test]
        public async Task SchemaQualifiedName_SplitsOnFirstDot()
        {
            var sql = MsSqlInnerSchemaService.QualifyTable("HangFire.JobParameter");
            await Assert.That(sql).IsEqualTo("[HangFire].[JobParameter]");
        }

        [Test]
        public async Task InvalidIdentifier_IsRejected()
        {
            await Assert.That(() => MsSqlInnerSchemaService.QualifyTable("HangFire.Job]Parameter"))
                .Throws<ArgumentException>();
        }
    }
}
