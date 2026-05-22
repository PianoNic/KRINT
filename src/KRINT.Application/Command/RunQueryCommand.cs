using Mediator;
using KRINT.Application.Dtos;
using KRINT.Infrastructure;
using KRINT.Infrastructure.Extensions;

namespace KRINT.Application.Command
{
    public record RunQueryCommand(Guid InstanceId, string Database, string Sql, int RowLimit) : ICommand<RunQueryResultDto>;

    public class RunQueryCommandHandler(
        KrintDbContext db,
        Infrastructure.Interfaces.ISecretsVaultService vault,
        IInnerQueryServiceResolver resolver)
        : ICommandHandler<RunQueryCommand, RunQueryResultDto>
    {
        public async ValueTask<RunQueryResultDto> Handle(RunQueryCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            var svc = resolver.TryResolve(target.Engine)
                ?? throw new NotSupportedException(
                    $"The query console is not available for engine '{target.Engine}' yet. " +
                    "Supported: postgres, timescaledb, pgvector, cockroachdb, mysql, mariadb, mssql, clickhouse.");

            var result = await svc.RunAsync(target, command.Database, command.Sql, command.RowLimit, cancellationToken);

            return new RunQueryResultDto
            {
                Columns = result.Columns.Select(c => new RunQueryColumnDto(c.Name, c.TypeName)).ToList(),
                Rows = result.Rows,
                RowsAffected = result.RowsAffected,
                ElapsedMs = result.ElapsedMs,
                Truncated = result.Truncated,
            };
        }
    }
}
