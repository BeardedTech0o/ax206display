using System.Net;
using System.Net.Sockets;

namespace Ax206Display.DataSources.Http;

/// <summary>
/// Decides whether a user-supplied integration address is acceptable before
/// the server connects to it. The web UI lets a signed-in user point the
/// server at an arbitrary host (to test a login, or to read its TLS
/// certificate), which makes the server a request proxy: without a check, that
/// could be aimed at a link-local cloud-metadata endpoint or at a non-HTTP
/// service. Loopback and private LAN addresses stay allowed on purpose, since a
/// Pi-hole on the same Pi or a UniFi console on the LAN is exactly the
/// intended use.
/// </summary>
public static class IntegrationTargetPolicy
{
    /// <summary>Returns an error message for a rejected address, or null when it is fine to connect.</summary>
    public static async Task<string?> CheckAsync(string? baseUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri))
        {
            return "Enter a valid host URL first.";
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return "Only http:// and https:// addresses are supported.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "Don't put a username or password in the address; use the separate fields.";
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return "Enter a valid host URL first.";
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
            }
            catch (SocketException)
            {
                // Not resolvable from here. Let the real connection attempt
                // report that; there is nothing to block.
                return null;
            }
        }

        return addresses.Any(IsBlockedAddress)
            ? "That address isn't allowed (link-local, unspecified or multicast addresses are blocked)."
            : null;
    }

    public static bool IsBlockedAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            // 169.254.0.0/16 (includes the 169.254.169.254 metadata endpoint)
            // and 224.0.0.0/4 multicast.
            return (bytes[0] == 169 && bytes[1] == 254) || (bytes[0] & 0xF0) == 224;
        }

        return address.IsIPv6LinkLocal || address.IsIPv6Multicast;
    }
}
