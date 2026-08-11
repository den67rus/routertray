using System.Net;

namespace RouterTray.Tests;

public sealed class NetworkInterfaceServiceTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void IsEligible_AcceptsAdaptersWithAnIpProtocol(bool supportsIpv4, bool supportsIpv6)
    {
        Assert.True(NetworkInterfaceService.IsEligible(
            System.Net.NetworkInformation.NetworkInterfaceType.Ethernet,
            supportsIpv4,
            supportsIpv6));
    }

    [Fact]
    public void IsEligible_RejectsProtocolIndependentFilterAdapters()
    {
        Assert.False(NetworkInterfaceService.IsEligible(
            System.Net.NetworkInformation.NetworkInterfaceType.Ethernet,
            supportsIpv4: false,
            supportsIpv6: false));
    }

    [Theory]
    [InlineData(System.Net.NetworkInformation.NetworkInterfaceType.Loopback)]
    [InlineData(System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)]
    public void IsEligible_RejectsLoopbackAndTunnelAdapters(
        System.Net.NetworkInformation.NetworkInterfaceType interfaceType)
    {
        Assert.False(NetworkInterfaceService.IsEligible(
            interfaceType,
            supportsIpv4: true,
            supportsIpv6: true));
    }

    [Fact]
    public async Task ResolveRouterAddressesAsync_ResolvesHostname()
    {
        var expected = new[] { IPAddress.Parse("192.168.50.1") };
        string? resolvedHost = null;

        var addresses = await NetworkInterfaceService.ResolveRouterAddressesAsync(
            new Uri("https://router.example:8443/"),
            (host, _) =>
            {
                resolvedHost = host;
                return Task.FromResult(expected);
            },
            CancellationToken.None);

        Assert.Equal("router.example", resolvedHost);
        Assert.Equal(expected, addresses);
    }

    [Fact]
    public async Task ResolveRouterAddressesAsync_DoesNotResolveIpLiteral()
    {
        var addresses = await NetworkInterfaceService.ResolveRouterAddressesAsync(
            new Uri("http://192.168.50.1/"),
            static (_, _) => throw new InvalidOperationException("Resolver must not be called."),
            CancellationToken.None);

        Assert.Equal(new[] { IPAddress.Parse("192.168.50.1") }, addresses);
    }
}
