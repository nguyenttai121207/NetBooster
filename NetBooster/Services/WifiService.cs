using System.Net.NetworkInformation;

namespace NetBooster.Services;

public sealed class WifiService
{
    public IReadOnlyList<NetworkAdapterInfo> GetAdapters() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .Select(n => new NetworkAdapterInfo(GetIndex(n), n.Name, n.Description, n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211, n.OperationalStatus == OperationalStatus.Up))
        .Where(n => n.InterfaceIndex >= 0)
        .OrderByDescending(n => n.IsUp).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public async Task<WifiInfo> GetCurrentAsync(NetworkAdapterInfo adapter)
    {
        if (!adapter.IsWifi || !TryGuid(adapter, out var id)) return WifiInfo.Unknown;
        try { return await Task.Run(() => NativeWifi.GetCurrent(id)); }
        catch { return WifiInfo.Unknown; }
    }

    public async Task<IReadOnlyList<WifiBss>> ScanAsync(NetworkAdapterInfo adapter, CancellationToken cancellationToken = default)
    {
        if (!adapter.IsWifi || !TryGuid(adapter, out var id)) return Array.Empty<WifiBss>();
        try { return await NativeWifi.ScanAsync(id, cancellationToken); }
        catch { return Array.Empty<WifiBss>(); }
    }

    private static int GetIndex(NetworkInterface adapter)
    {
        try { return adapter.GetIPProperties().GetIPv4Properties().Index; } catch { return -1; }
    }
    private static bool TryGuid(NetworkAdapterInfo adapter, out Guid id) => Guid.TryParse(NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => GetIndex(n) == adapter.InterfaceIndex)?.Id, out id);
}
