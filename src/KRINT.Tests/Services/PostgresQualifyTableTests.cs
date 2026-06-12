using KRINT.Infrastructure.Services;

namespace KRINT.Tests.Services
{
    /// <summary>
    /// Non-public Postgres tables are browsed as "schema.name"; the unqualified form only
    /// resolves in public via search_path. Regressing this made browsing Hangfire/TickerQ
    /// tables 500 with 42P01 (issue #136).
    /// </summary>
    public class PostgresQualifyTableTests
    {
        [Test]
        public async Task BareName_QualifiesAsPublic()
        {
            var sql = PostgresInnerSchemaService.QualifyTable("Transcripts");
            await Assert.That(sql).IsEqualTo("\"public\".\"Transcripts\"");
        }

        [Test]
        public async Task SchemaQualifiedName_SplitsOnFirstDot()
        {
            var sql = PostgresInnerSchemaService.QualifyTable("hangfire.jobparameter");
            await Assert.That(sql).IsEqualTo("\"hangfire\".\"jobparameter\"");
        }

        [Test]
        public async Task InvalidIdentifier_IsRejected()
        {
            await Assert.That(() => PostgresInnerSchemaService.QualifyTable("hangfire.job\"parameter"))
                .Throws<ArgumentException>();
        }
    }
}
