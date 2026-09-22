using System.Net;
using System.Net.Sockets;

namespace KRINT.Infrastructure.Services
{
    /// <summary>
    /// Refuses probe targets that are never a database and always a secret: the addresses cloud
    /// metadata services live on. Private networks stay allowed on purpose - reaching databases on
    /// the host's own network is what registering an external instance is for. A host name is
    /// resolved and every address it maps to is checked, so a name pointing at the metadata range
    /// is caught the same way a literal is; integer and hex spellings of an address parse to the
    /// same IPAddress and need no special casing.
    /// </summary>
    public static class ProbeHostGuard
    {
        private static readonly string[] BlockedNames =
        {
            "metadata.google.internal",
            "metadata",
            "instance-data",
        };

        // Well-known metadata endpoints outside the link-local range.
        private static readonly IPAddress[] BlockedAddresses =
        {
            IPAddress.Parse("100.100.100.200"),   // Alibaba Cloud
            IPAddress.Parse("fd00:ec2::254"),     // AWS IMDS over IPv6
        };

        public static async Task RequireAsync(string host, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Host is required.", nameof(host));

            var trimmed = host.Trim().TrimStart('[').TrimEnd(']');

            if (BlockedNames.Any(name => string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Host '{host}' is a cloud metadata endpoint, not a database.", nameof(host));

            if (IPAddress.TryParse(trimmed, out var literal))
            {
                Check(host, literal);
                return;
            }

            IPAddress[] resolved;
            try
            {
                resolved = await Dns.GetHostAddressesAsync(trimmed, cancellationToken);
            }
            catch (SocketException)
            {
                // Unresolvable now; the connection attempt that follows will say so in its own words.
                return;
            }

            foreach (var address in resolved)
                Check(host, address);
        }

        private static void Check(string host, IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

            if (IsLinkLocal(address) || BlockedAddresses.Any(b => b.Equals(address)))
                throw new ArgumentException($"Host '{host}' resolves to {address}, a metadata or link-local address KRINT will not probe.", nameof(host));

            if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                throw new ArgumentException($"Host '{host}' is the unspecified address.", nameof(host));
        }

        private static bool IsLinkLocal(IPAddress ip)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                return b[0] == 169 && b[1] == 254;
            }
            return ip.IsIPv6LinkLocal;
        }
    }
}
