using Mediator;
using Microsoft.EntityFrameworkCore;
using KRINT.Application.Dtos;
using KRINT.Infrastructure;

namespace KRINT.Application.Queries
{
    public record ListBackupsQuery(Guid? InstanceId = null) : IQuery<IReadOnlyList<BackupEntryDto>>;

    public class ListBackupsQueryHandler(KrintDbContext db)
        : IQueryHandler<ListBackupsQuery, IReadOnlyList<BackupEntryDto>>
    {
        public async ValueTask<IReadOnlyList<BackupEntryDto>> Handle(ListBackupsQuery query, CancellationToken cancellationToken)
        {
            var q = db.BackupEntries.AsQueryable();
            if (query.InstanceId is { } id) q = q.Where(b => b.InstanceId == id);

            return await q
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new BackupEntryDto
                {
                    Id = b.Id,
                    InstanceId = b.InstanceId,
                    Engine = b.Engine,
                    FileName = b.FileName,
                    SizeBytes = b.SizeBytes,
                    CreatedAt = b.CreatedAt,
                })
                .ToListAsync(cancellationToken);
        }
    }
}
