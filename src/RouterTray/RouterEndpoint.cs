using System.Net;
using System.Net.Sockets;

namespace RouterTray;

internal static class RouterEndpoint
{
    public static string NormalizeConfiguredUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.GetLeftPart(UriPartial.Authority).Contains('@') ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            candidate.Contains('?') ||
            candidate.Contains('#'))
        {
            throw new InvalidOperationException(
                "RouterUrl must be an absolute HTTP or HTTPS URL without credentials, query, or fragment.");
        }

        return candidate.EndsWith('/') ? candidate : candidate + "/";
    }

    public static Uri? GetConfiguredUri(string? value)
    {
        var normalized = NormalizeConfiguredUrl(value);
        return string.IsNullOrEmpty(normalized) ? null : new Uri(normalized, UriKind.Absolute);
    }

    public static Uri? Resolve(string? configuredUrl, string? activeGateway)
    {
        var configured = GetConfiguredUri(configuredUrl);
        if (configured is not null)
        {
            return configured;
        }

        return string.IsNullOrWhiteSpace(activeGateway)
            ? null
            : CreateGatewayUri(activeGateway);
    }

    public static Uri CreateGatewayUri(string gateway)
    {
        if (!IPAddress.TryParse(gateway, out var address))
        {
            throw new InvalidOperationException("Gateway must be a valid IP address.");
        }

        var host = address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{gateway}]"
            : gateway;
        return new Uri($"http://{host}/", UriKind.Absolute);
    }

    public static bool Equals(Uri? left, Uri? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
               left.Port == right.Port &&
               string.Equals(left.AbsolutePath, right.AbsolutePath, StringComparison.Ordinal);
    }
}
