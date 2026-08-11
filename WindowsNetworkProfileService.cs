using System.Runtime.InteropServices;

namespace RouterTray;

internal sealed class WindowsNetworkProfileService
{
    private static readonly Guid NetworkListManagerClassId =
        new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

    public Task<IReadOnlyDictionary<Guid, WindowsNetworkIdentity>> GetConnectedNetworksAsync(
        CancellationToken ct = default)
    {
        return Task.Run(() => QueryConnectedNetworks(ct), ct);
    }

    private static IReadOnlyDictionary<Guid, WindowsNetworkIdentity> QueryConnectedNetworks(
        CancellationToken ct)
    {
        var identities = new Dictionary<Guid, WindowsNetworkIdentity>();
        object? managerObject = null;
        IEnumNetworks? networks = null;

        try
        {
            ct.ThrowIfCancellationRequested();

            var managerType = Type.GetTypeFromCLSID(NetworkListManagerClassId, throwOnError: true);
            managerObject = Activator.CreateInstance(managerType!);
            var manager = (INetworkListManager)managerObject!;
            networks = manager.GetNetworks(NetworkEnumeration.Connected);

            while (TryGetNext(networks, out var network))
            {
                IEnumNetworkConnections? connections = null;

                try
                {
                    ct.ThrowIfCancellationRequested();

                    var networkId = network.GetNetworkId();
                    var networkName = network.GetName()?.Trim() ?? string.Empty;
                    connections = network.GetNetworkConnections();

                    while (TryGetNext(connections, out var connection))
                    {
                        try
                        {
                            var adapterId = connection.GetAdapterId();
                            identities[adapterId] = new WindowsNetworkIdentity(
                                adapterId,
                                networkId.ToString("D"),
                                networkName);
                        }
                        finally
                        {
                            ReleaseComObject(connection);
                        }
                    }
                }
                finally
                {
                    ReleaseComObject(connections);
                    ReleaseComObject(network);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or
                                   InvalidCastException or
                                   PlatformNotSupportedException or
                                   UnauthorizedAccessException)
        {
            // Network identification is an enhancement. Interface and gateway
            // discovery must remain usable if Network List Manager is unavailable.
        }
        finally
        {
            ReleaseComObject(networks);
            ReleaseComObject(managerObject);
        }

        return identities;
    }

    private static bool TryGetNext(IEnumNetworks networks, out INetwork network)
    {
        uint fetched = 0;
        networks.Next(1, out network, ref fetched);
        return fetched != 0 && network is not null;
    }

    private static bool TryGetNext(
        IEnumNetworkConnections connections,
        out INetworkConnection connection)
    {
        uint fetched = 0;
        connections.Next(1, out connection, ref fetched);
        return fetched != 0 && connection is not null;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    [Flags]
    private enum NetworkEnumeration
    {
        Connected = 1
    }

    private enum NetworkDomainType
    {
        NonDomainNetwork = 0,
        DomainNetwork = 1,
        DomainAuthenticated = 2
    }

    [Flags]
    private enum NetworkConnectivity
    {
        Disconnected = 0
    }

    [ComImport]
    [Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
    [TypeLibType(TypeLibTypeFlags.FDual | TypeLibTypeFlags.FDispatchable)]
    private interface INetworkListManager
    {
        [DispId(1)]
        [return: MarshalAs(UnmanagedType.Interface)]
        IEnumNetworks GetNetworks([In] NetworkEnumeration flags);
    }

    [ComImport]
    [Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
    [TypeLibType(TypeLibTypeFlags.FDual | TypeLibTypeFlags.FDispatchable)]
    private interface INetwork
    {
        [DispId(1)]
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetName();

        [DispId(2)]
        void SetName([In, MarshalAs(UnmanagedType.BStr)] string name);

        [DispId(3)]
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetDescription();

        [DispId(4)]
        void SetDescription([In, MarshalAs(UnmanagedType.BStr)] string description);

        [DispId(5)]
        Guid GetNetworkId();

        [DispId(6)]
        NetworkDomainType GetDomainType();

        [DispId(7)]
        [return: MarshalAs(UnmanagedType.Interface)]
        IEnumNetworkConnections GetNetworkConnections();
    }

    [ComImport]
    [Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B")]
    [TypeLibType(TypeLibTypeFlags.FDual | TypeLibTypeFlags.FDispatchable)]
    private interface INetworkConnection
    {
        [DispId(1)]
        [return: MarshalAs(UnmanagedType.Interface)]
        INetwork GetNetwork();

        [DispId(2)]
        bool get_IsConnectedToInternet();

        [DispId(3)]
        bool get_IsConnected();

        [DispId(4)]
        NetworkConnectivity GetConnectivity();

        [DispId(5)]
        Guid GetConnectionId();

        [DispId(6)]
        Guid GetAdapterId();
    }

    [ComImport]
    [Guid("DCB00003-570F-4A9B-8D69-199FDBA5723B")]
    [TypeLibType(TypeLibTypeFlags.FDual | TypeLibTypeFlags.FDispatchable)]
    private interface IEnumNetworks
    {
        [DispId(-4)]
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetEnumerator();

        [DispId(1)]
        void Next(
            [In] uint count,
            [Out, MarshalAs(UnmanagedType.Interface)] out INetwork network,
            [In, Out] ref uint fetched);
    }

    [ComImport]
    [Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B")]
    [TypeLibType(TypeLibTypeFlags.FDual | TypeLibTypeFlags.FDispatchable)]
    private interface IEnumNetworkConnections
    {
        [DispId(-4)]
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetEnumerator();

        [DispId(1)]
        void Next(
            [In] uint count,
            [Out, MarshalAs(UnmanagedType.Interface)] out INetworkConnection connection,
            [In, Out] ref uint fetched);
    }
}

internal sealed record WindowsNetworkIdentity(
    Guid AdapterId,
    string NetworkId,
    string Name);
