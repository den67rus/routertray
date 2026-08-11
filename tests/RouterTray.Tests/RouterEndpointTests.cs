using System.Net.Sockets;

namespace RouterTray.Tests;

public sealed class RouterEndpointTests
{
    [Fact]
    public void NormalizeConfiguredUrl_PreservesHostPortAndPath()
    {
        var normalized = RouterEndpoint.NormalizeConfiguredUrl(
            "https://router.example:8443/custom/api");

        Assert.Equal("https://router.example:8443/custom/api/", normalized);
    }

    [Fact]
    public void NormalizeConfiguredUrl_AddsSlashToRootUrl()
    {
        Assert.Equal(
            "https://router.example/",
            RouterEndpoint.NormalizeConfiguredUrl("https://router.example"));
    }

    [Theory]
    [InlineData("file:///C:/router")]
    [InlineData("ftp://192.168.1.1")]
    [InlineData("https://user:password@router.example/")]
    [InlineData("https://@router.example/")]
    [InlineData("https://router.example/?token=value")]
    [InlineData("https://router.example/?")]
    [InlineData("https://router.example/#fragment")]
    [InlineData("https://router.example/#")]
    public void NormalizeConfiguredUrl_RejectsUnsafeOrUnsupportedUrls(string value)
    {
        Assert.Throws<InvalidOperationException>(() => RouterEndpoint.NormalizeConfiguredUrl(value));
    }

    [Fact]
    public void Resolve_AlwaysHonorsExplicitUrl()
    {
        var result = RouterEndpoint.Resolve(
            "https://router.example:8443/api/",
            "192.168.1.1");

        Assert.NotNull(result);
        Assert.Equal("router.example", result.Host);
        Assert.Equal(8443, result.Port);
        Assert.Equal("/api/", result.AbsolutePath);
    }

    [Fact]
    public void Resolve_ReturnsNullWhenAutomaticGatewayIsUnavailable()
    {
        Assert.Null(RouterEndpoint.Resolve(string.Empty, null));
    }

    [Fact]
    public void Equals_TreatsPathAsCaseSensitive()
    {
        Assert.False(RouterEndpoint.Equals(
            new Uri("https://router.example/API/"),
            new Uri("https://router.example/api/")));
    }

    [Theory]
    [InlineData("192.168.1.1", AddressFamily.InterNetwork)]
    [InlineData("fe80::1", AddressFamily.InterNetworkV6)]
    [InlineData("fe80::1%12", AddressFamily.InterNetworkV6)]
    public void CreateGatewayUri_HandlesIpv4AndIpv6(string gateway, AddressFamily family)
    {
        var result = RouterEndpoint.CreateGatewayUri(gateway);

        Assert.Equal(Uri.UriSchemeHttp, result.Scheme);
        Assert.True(System.Net.IPAddress.TryParse(result.DnsSafeHost, out var address));
        Assert.Equal(family, address.AddressFamily);
        if (family == AddressFamily.InterNetworkV6)
        {
            Assert.StartsWith("http://[", result.OriginalString, StringComparison.Ordinal);
        }
    }
}
