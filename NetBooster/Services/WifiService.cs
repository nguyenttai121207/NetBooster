using System.Net.NetworkInformation;

namespace NetBooster.Services;

public sealed class WifiService
{
    private static readonly string[] VirtualKeywords = new[]
    {
        "vmware", "virtualbox", "hyper-v", "tap", "vpn", "wireguard", "virtual", "loopback", "npcap", "wsl", "bluetooth"
    };

    public IReadOnlyList<NetworkAdapterInfo> GetAdapters() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .Where(n => !IsVirtual(n))
        .Select(n => new NetworkAdapterInfo(
            GetIndex(n),
            n.Name,
            n.Description,
            n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
            n.OperationalStatus == OperationalStatus.Up,
            n.Id))
        .Where(n => n.InterfaceIndex >= 0)
        .OrderByDescending(n => n.IsUp)
        .ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

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

    private static bool IsVirtual(NetworkInterface adapter)
    {
        var desc = adapter.Description.ToLowerInvariant();
        var name = adapter.Name.ToLowerInvariant();
        return VirtualKeywords.Any(k => desc.Contains(k) || name.Contains(k));
    }

    private static int GetIndex(NetworkInterface adapter)
    {
        try { return adapter.GetIPProperties().GetIPv4Properties().Index; } catch { return -1; }
    }

    public static bool TryGuid(NetworkAdapterInfo adapter, out Guid id)
    {
        if (!string.IsNullOrWhiteSpace(adapter.Id) && Guid.TryParse(adapter.Id, out id))
            return true;
        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => GetIndex(n) == adapter.InterfaceIndex);
        if (nic != null && Guid.TryParse(nic.Id, out id))
            return true;
        id = Guid.Empty;
        return false;
    }
}
