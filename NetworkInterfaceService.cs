using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;

namespace RouterTray;

internal sealed class NetworkInterfaceService
{

    public InterfaceSnapshot GetSnapshot()
    {
        var interfaces = new List<NetworkInterfaceInfo>();
        var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
        var localAddress = TryGetLocalAddressForDefaultRoute();
        var activeInterface = localAddress is null
            ? null
            : FindInterfaceByAddress(allInterfaces, localAddress);

        if (activeInterface is null)
        {
            activeInterface = FindInterfaceWithGateway(allInterfaces);
        }

        var activeGateway = activeInterface is null ? null : GetDefaultGateway(activeInterface);

        foreach (var netInterface in allInterfaces)
        {
            if (netInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                netInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
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

            interfaces.Add(new NetworkInterfaceInfo(
                netInterface.Name,
                netInterface.Description,
                macAddress,
                isUp,
                isActive));
        }

        var activeMac = interfaces.FirstOrDefault(info => info.IsActive)?.MacAddress;
        var ordered = interfaces
            .OrderByDescending(info => info.IsActive)
            .ThenByDescending(info => info.IsUp)
            .ThenBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new InterfaceSnapshot(ordered, activeMac, activeGateway);
    }

    public string GetRouterUrl(string fallbackRouterUrl)
    {
        var snapshot = GetSnapshot();
        if (string.IsNullOrWhiteSpace(snapshot.ActiveGateway))
        {
            return fallbackRouterUrl;
        }

        var scheme = TryGetScheme(fallbackRouterUrl) ?? "http";
        return $"{scheme}://{snapshot.ActiveGateway}";
    }

    private static bool InterfaceHasAddress(NetworkInterface netInterface, IPAddress address)
    {
        var properties = netInterface.GetIPProperties();
        foreach (var entry in properties.UnicastAddresses)
        {
            if (entry.Address.Equals(address))
            {
                return true;
            }
        }

        return false;
    }

    private static IPAddress? TryGetLocalAddressForDefaultRoute()
    {
        var local = TryGetLocalAddressForRemote(new IPAddress(new byte[] { 1, 1, 1, 1 }));
        if (local is not null)
        {
            return local;
        }

        var ipv6 = IPAddress.Parse("2606:4700:4700::1111");
        return TryGetLocalAddressForRemote(ipv6);
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
            if (netInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                netInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            if (InterfaceHasAddress(netInterface, localAddress))
            {
                return netInterface;
            }
        }

        return null;
    }

    private static NetworkInterface? FindInterfaceWithGateway(IEnumerable<NetworkInterface> interfaces)
    {
        foreach (var netInterface in interfaces)
        {
            if (netInterface.OperationalStatus != OperationalStatus.Up ||
                netInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                netInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var gateway = GetDefaultGateway(netInterface);
            if (!string.IsNullOrWhiteSpace(gateway))
            {
                return netInterface;
            }
        }

        return null;
    }

    private static string? GetDefaultGateway(NetworkInterface netInterface)
    {
        var properties = netInterface.GetIPProperties();
        var gateways = properties.GatewayAddresses
            .Select(gateway => gateway.Address)
            .Where(address => address is not null && !IPAddress.IsLoopback(address))
            .ToArray();

        var ipv4 = gateways.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
        if (ipv4 is not null && !ipv4.Equals(IPAddress.Any))
        {
            return ipv4.ToString();
        }

        var ipv6 = gateways.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetworkV6);
        return ipv6?.ToString();
    }

    private static string? TryGetScheme(string? routerUrl)
    {
        if (string.IsNullOrWhiteSpace(routerUrl))
        {
            return null;
        }

        if (Uri.TryCreate(routerUrl, UriKind.Absolute, out var uri))
        {
            return uri.Scheme;
        }

        return null;
    }

    private static string FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(":", bytes.Select(value => value.ToString("X2")));
    }
}

internal sealed class NetworkInterfaceInfo
{
    public NetworkInterfaceInfo(string name, string description, string macAddress, bool isUp, bool isActive)
    {
        Name = name;
        Description = description;
        MacAddress = macAddress;
        IsUp = isUp;
        IsActive = isActive;
    }

    public string Name { get; }
    public string Description { get; }
    public string MacAddress { get; }
    public bool IsUp { get; }
    public bool IsActive { get; }
}

internal sealed class InterfaceSnapshot
{
    public InterfaceSnapshot(IReadOnlyList<NetworkInterfaceInfo> interfaces, string? activeMac, string? activeGateway)
    {
        Interfaces = interfaces;
        ActiveMac = activeMac;
        ActiveGateway = activeGateway;
    }

    public IReadOnlyList<NetworkInterfaceInfo> Interfaces { get; }
    public string? ActiveMac { get; }
    public string? ActiveGateway { get; }
}
