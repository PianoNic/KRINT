using System.Net;
using System.Net.Sockets;

namespace KRINT.Infrastructure.Services
{
    /// <summary>
    /// Refuses probe targets that are never a database and always a secret: the link-local range
    /// that cloud metadata services live on. Private networks stay allowed on purpose - reaching
    /// databases on the host's own network is what registering an external instance is for.
    /// </summary>
    public static class ProbeHostGuard
    {
        private static readonly string[] BlockedNames =
        {
            "metadata.google.internal",
            "metadata",
        };

        public static void Require(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("Host is required.", nameof(host));

            var trimmed = host.Trim().TrimStart('[').TrimEnd(']');

            if (BlockedNames.Any(name => string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Host '{host}' is a cloud metadata endpoint, not a database.", nameof(host));

            if (IPAddress.TryParse(trimmed, out var ip) && IsLinkLocal(ip))
                throw new ArgumentException($"Host '{host}' is a link-local address, which KRINT will not probe.", nameof(host));
        }

        private static bool IsLinkLocal(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = ip.GetAddressBytes();
                return b[0] == 169 && b[1] == 254;
            }
            return ip.IsIPv6LinkLocal;
        }
    }
}
