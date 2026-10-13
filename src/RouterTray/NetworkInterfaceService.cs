using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RouterTray;

internal sealed class NetworkInterfaceService
{
    private static readonly IPAddress DefaultIpv4Probe = IPAddress.Parse("1.1.1.1");
    private static readonly IPAddress DefaultIpv6Probe = IPAddress.Parse("2606:4700:4700::1111");
    private readonly WindowsNetworkProfileService _networkProfileService = new();

    public async Task<InterfaceSnapshot> GetSnapshotAsync(
        string? preferredInterfaceId = null,
        Uri? configuredRouterUri = null,
        CancellationToken ct = default)
    {
        var networkProfilesTask = _networkProfileService.GetConnectedNetworksAsync(ct);
        var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
        var activeInterface = FindPreferredInterface(allInterfaces, preferredInterfaceId);

        if (activeInterface is null && configuredRouterUri is not null)
        {
            activeInterface = await FindInterfaceForRouterAsync(
                allInterfaces,
                configuredRouterUri,
                ct).ConfigureAwait(false);
        }

        if (activeInterface is null)
        {
            activeInterface = FindInterfaceForRemote(allInterfaces, DefaultIpv4Probe) ??
                              FindInterfaceForRemote(allInterfaces, DefaultIpv6Probe) ??
                              FindInterfaceWithGateway(allInterfaces);
        }

        var activeGateway = activeInterface is null ? null : GetDefaultGateway(activeInterface);
        var networkProfiles = await networkProfilesTask.ConfigureAwait(false);
        var interfaces = new List<NetworkInterfaceInfo>();

        foreach (var netInterface in allInterfaces)
        {
            if (!IsEligible(netInterface))
            {
                continue;
            }

            var macAddress = FormatMac(netInterface.GetPhysicalAddress());
            if (string.IsNullOrWhiteSpace(macAddress))
            {
                continue;
            }

            var isUp = netInterface.OperationalStatus == OperationalStatus.Up;
            var isActive = isUp && activeInterface is not null &&
                           string.Equals(netInterface.Id, activeInterface.Id, StringComparison.OrdinalIgnoreCase);
            var isPreferred = !string.IsNullOrWhiteSpace(preferredInterfaceId) &&
                              string.Equals(
                                  netInterface.Id,
                                  preferredInterfaceId,
                                  StringComparison.OrdinalIgnoreCase);
            var networkIdentity = Guid.TryParse(netInterface.Id, out var adapterId) &&
                                  networkProfiles.TryGetValue(adapterId, out var identity)
                ? identity
                : null;

            interfaces.Add(new NetworkInterfaceInfo(
                netInterface.Id,
                netInterface.Name,
                netInterface.Description,
                macAddress,
                GetDefaultGateway(netInterface),
                networkIdentity?.NetworkId,
                networkIdentity?.Name,
                isUp,
                isActive,
                isPreferred));
        }

        var activeInfo = interfaces.FirstOrDefault(info => info.IsActive);
        var ordered = interfaces
            .OrderByDescending(info => info.IsActive)
            .ThenByDescending(info => info.IsPreferred)
            .ThenByDescending(info => info.IsUp)
            .ThenBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new InterfaceSnapshot(
            ordered,
            activeInfo?.Id,
            activeInfo?.MacAddress,
            activeGateway,
            activeInfo?.NetworkId,
            activeInfo?.NetworkName);
    }

    public Uri? ResolveRouterUri(string configuredRouterUrl, InterfaceSnapshot snapshot)
    {
        return RouterEndpoint.Resolve(configuredRouterUrl, snapshot.ActiveGateway);
    }

    private static NetworkInterface? FindPreferredInterface(
        IEnumerable<NetworkInterface> interfaces,
        string? preferredInterfaceId)
    {
        if (string.IsNullOrWhiteSpace(preferredInterfaceId))
        {
            return null;
        }

        return interfaces.FirstOrDefault(netInterface =>
            IsEligible(netInterface) &&
            netInterface.OperationalStatus == OperationalStatus.Up &&
            string.Equals(netInterface.Id, preferredInterfaceId, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<NetworkInterface?> FindInterfaceForRouterAsync(
        IEnumerable<NetworkInterface> interfaces,
        Uri routerUri,
        CancellationToken ct)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await ResolveRouterAddressesAsync(
                routerUri,
                static (host, token) => Dns.GetHostAddressesAsync(host, token),
                ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return null;
        }

        foreach (var address in addresses)
        {
            var netInterface = FindInterfaceForRemote(interfaces, address);
            if (netInterface is not null)
            {
                return netInterface;
            }
        }

        return null;
    }

    internal static Task<IPAddress[]> ResolveRouterAddressesAsync(
        Uri routerUri,
        Func<string, CancellationToken, Task<IPAddress[]>> resolver,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(routerUri);
        ArgumentNullException.ThrowIfNull(resolver);

        return IPAddress.TryParse(routerUri.DnsSafeHost, out var address)
            ? Task.FromResult(new[] { address })
            : resolver(routerUri.DnsSafeHost, ct);
    }

    private static NetworkInterface? FindInterfaceForRemote(
        IEnumerable<NetworkInterface> interfaces,
        IPAddress remoteAddress)
    {
        var localAddress = TryGetLocalAddressForRemote(remoteAddress);
        return localAddress is null ? null : FindInterfaceByAddress(interfaces, localAddress);
    }

    private static IPAddress? TryGetLocalAddressForRemote(IPAddress remote)
    {
        try
        {
            using var socket = new Socket(remote.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(remote, 65530));
            return (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static NetworkInterface? FindInterfaceByAddress(
        IEnumerable<NetworkInterface> interfaces,
        IPAddress localAddress)
    {
        foreach (var netInterface in interfaces)
        {
            if (!IsEligible(netInterface))
            {
                continue;
            }

            if (netInterface.GetIPProperties().UnicastAddresses.Any(entry => entry.Address.Equals(localAddress)))
            {
                return netInterface;
            }
        }

        return null;
    }

    private static NetworkInterface? FindInterfaceWithGateway(IEnumerable<NetworkInterface> interfaces)
    {
        return interfaces.FirstOrDefault(netInterface =>
            IsEligible(netInterface) &&
            netInterface.OperationalStatus == OperationalStatus.Up &&
            !string.IsNullOrWhiteSpace(GetDefaultGateway(netInterface)));
    }

    private static bool IsEligible(NetworkInterface netInterface)
    {
        return IsEligible(
            netInterface.NetworkInterfaceType,
            netInterface.Supports(NetworkInterfaceComponent.IPv4),
            netInterface.Supports(NetworkInterfaceComponent.IPv6));
    }

    internal static bool IsEligible(
        NetworkInterfaceType interfaceType,
        bool supportsIpv4,
        bool supportsIpv6)
    {
        return interfaceType != NetworkInterfaceType.Loopback &&
               interfaceType != NetworkInterfaceType.Tunnel &&
               (supportsIpv4 || supportsIpv6);
    }

    private static string? GetDefaultGateway(NetworkInterface netInterface)
    {
        var gateways = netInterface.GetIPProperties().GatewayAddresses
            .Select(gateway => gateway.Address)
            .Where(address => !IPAddress.IsLoopback(address))
            .ToArray();

        var ipv4 = gateways.FirstOrDefault(address =>
            address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any));
        if (ipv4 is not null)
        {
            return ipv4.ToString();
        }

        return gateways.FirstOrDefault(address =>
            address.AddressFamily == AddressFamily.InterNetworkV6)?.ToString();
    }

    private static string FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0
            ? string.Empty
            : string.Join(":", bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
    }

}

internal sealed record NetworkInterfaceInfo(
    string Id,
    string Name,
    string Description,
    string MacAddress,
    string? Gateway,
    string? NetworkId,
    string? NetworkName,
    bool IsUp,
    bool IsActive,
    bool IsPreferred);

internal sealed record InterfaceSnapshot(
    IReadOnlyList<NetworkInterfaceInfo> Interfaces,
    string? ActiveInterfaceId,
    string? ActiveMac,
    string? ActiveGateway,
    string? ActiveNetworkId,
    string? ActiveNetworkName);
