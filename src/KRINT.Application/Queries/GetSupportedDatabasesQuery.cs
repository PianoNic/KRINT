using Mediator;
using KRINT.Application.Dtos;
using KRINT.Infrastructure.Interfaces;

namespace KRINT.Application.Queries
{
    public record GetSupportedDatabasesQuery : IQuery<IReadOnlyList<SupportedDatabaseDto>>;

    public class GetSupportedDatabasesQueryHandler : IQueryHandler<GetSupportedDatabasesQuery, IReadOnlyList<SupportedDatabaseDto>>
    {
        private static readonly (string Key, string DisplayName, string Image)[] Engines = new[]
        {
            ("postgres", "PostgreSQL", "postgres"),
            ("mysql", "MySQL", "mysql"),
            ("mariadb", "MariaDB", "mariadb"),
            ("mongo", "MongoDB", "mongo"),
        };

        private readonly IDatabaseVersionService _versions;

        public GetSupportedDatabasesQueryHandler(IDatabaseVersionService versions)
        {
            _versions = versions;
        }

        public async ValueTask<IReadOnlyList<SupportedDatabaseDto>> Handle(GetSupportedDatabasesQuery query, CancellationToken cancellationToken)
        {
            var result = new List<SupportedDatabaseDto>(Engines.Length);
            foreach (var (key, displayName, image) in Engines)
            {
                var versions = await _versions.GetSupportedVersionsAsync(key, cancellationToken);
                result.Add(new SupportedDatabaseDto
                {
                    Key = key,
                    DisplayName = displayName,
                    Image = image,
                    Versions = versions,
                });
            }
            return result;
        }
    }
}
