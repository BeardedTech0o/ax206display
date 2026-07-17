namespace Ax206Display.DataSources.Http;

/// <summary>
/// Cleans up a "just the hostname/IP" input field against the very natural
/// mistake of typing what looks like a URL into it (e.g. "http://pi.hole").
/// Naively concatenating that into "scheme://" + (already-schemed host) +
/// ":port" produces a malformed double-scheme string; .NET's Uri parser then
/// reads the first scheme token itself as the hostname and falls back to
/// that scheme's default port, failing as "no such host" with no hint of
/// what actually went wrong. Strips any scheme, path, port, query, or
/// fragment the user included, keeping only the actual host.
/// </summary>
public static class HostNormalizer
{
    public static string Normalize(string rawHost)
    {
        var host = rawHost.Trim();

        var schemeSeparatorIndex = host.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparatorIndex >= 0)
        {
            host = host[(schemeSeparatorIndex + 3)..];
        }

        var endIndex = host.IndexOfAny(['/', ':', '?', '#']);
        if (endIndex >= 0)
        {
            host = host[..endIndex];
        }

        return host;
    }
}
