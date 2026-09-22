using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using KRINT.Application.Dtos.Settings;
using KRINT.Application.Options;
using KRINT.Application.Queries.SupportedDatabase;

namespace KRINT.Application.Queries.Settings
{
    public record GetSettingsQuery : IQuery<SettingsDto>;

    public class GetSettingsQueryHandler(IOptions<KrintOptions> options, IConfiguration configuration, IMediator mediator) : IQueryHandler<GetSettingsQuery, SettingsDto>
    {
        // "Configured" means a key that actually decodes, not merely a value that is present:
        // a 16-byte key shows green on the settings page and fails on the first provision.
        private static bool VaultKeyDecodes(IConfiguration configuration)
        {
            try { KRINT.Infrastructure.Services.SecretsVaultService.ValidateMasterKey(configuration); return true; }
            catch (InvalidOperationException) { return false; }
        }

        public async ValueTask<SettingsDto> Handle(GetSettingsQuery query, CancellationToken cancellationToken)
        {
            var supported = await mediator.Send(new GetSupportedDatabasesQuery(), cancellationToken);

            var ranges = options.Value.PortRanges
                .Select(kv =>
                {
                    var range = PortRange.Parse(kv.Key, kv.Value);
                    return new PortRangeDto { Engine = kv.Key, Start = range.Start, End = range.End };
                })
                .OrderBy(r => r.Engine, StringComparer.Ordinal)
                .ToList();

            return new SettingsDto
            {
                PortRanges = ranges,
                SupportedEngines = supported,
                VaultMasterKeyConfigured = VaultKeyDecodes(configuration),
            };
        }
    }
}
