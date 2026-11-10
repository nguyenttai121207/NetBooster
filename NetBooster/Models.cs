using System.Net;

namespace NetBooster;

public sealed record NetworkAdapterInfo(int InterfaceIndex, string Name, string Description, bool IsWifi, bool IsUp);

public sealed record PingSummary(
    string Target,
    int Sent,
    int Received,
    double LossPercent,
    double MedianMs,
    double JitterMs,
    string? Error = null);

public sealed record WifiInfo(
    string Ssid,
    string Bssid,
    int? SignalPercent,
    string RadioType,
    int? Channel,
    string Band,
    double? ReceiveMbps,
    double? TransmitMbps,
    bool IsConnected)
{
    public static WifiInfo Unknown => new("Không xác định", "Không xác định", null, "Không xác định", null, "Không xác định", null, null, false);
}

public sealed record WifiBss(string Ssid, string Bssid, int? Channel, string Band, int SignalPercent);
public sealed record ChannelRecommendation(string Band, int Channel, int NearbyNetworks, string Advice);

public enum CacheState { Cold, Hot }
public sealed record DnsMeasurement(string Server, AddressFamily AddressFamily, CacheState CacheState, double MedianMs, double FailurePercent, int Samples);
public sealed record DnsProvider(string Id, string DisplayName, string[] IPv4, string[] IPv6);
public sealed record DnsProviderBenchmark(DnsProvider Provider, IReadOnlyList<DnsMeasurement> Measurements)
{
    public double MedianMs
    {
        get
        {
            var values = Measurements.Where(m => m.MedianMs >= 0).Select(m => m.MedianMs).OrderBy(x => x).ToArray();
            return values.Length == 0 ? -1 : values[values.Length / 2];
        }
    }
    public double FailurePercent => Measurements.Count == 0 ? 100 : Measurements.Average(m => m.FailurePercent);
}
public sealed record DnsDecision(bool ShouldApply, string Reason, DnsProvider? Winner);
public sealed record DnsBackup(int InterfaceIndex, bool Ipv4Dhcp, bool Ipv6Dhcp, string[] Ipv4Addresses, string[] Ipv6Addresses, DateTime SavedAtUtc);

public sealed record BufferbloatEndpointResult(PingSummary Baseline, PingSummary Loaded, double MedianIncreaseMs, double JitterIncreaseMs);
public sealed record BufferbloatResult(BufferbloatEndpointResult? Gateway, BufferbloatEndpointResult Internet, long DownloadedBytes, string? LoadError, string Conclusion);

public sealed record AdapterPowerState(string? SelectiveSuspend, string? DeviceSleepOnDisconnect, string? D0PacketCoalescing, string? WakeOnMagicPacket, string? WakeOnPattern);
public sealed record AdapterPowerBackup(int InterfaceIndex, AdapterPowerState State, DateTime SavedAtUtc);
public sealed record PowerPlanBackup(Guid SchemeGuid, DateTime SavedAtUtc);
public sealed record DeliveryOptimizationBackup(int? BackgroundPercent, DateTime SavedAtUtc);
public sealed record DeliveryOptimizationState(bool IsSupported, int? BackgroundPercent, string Message);

public sealed record NetworkProcessActivity(int ProcessId, string ProcessName, int ActiveTcpConnections, DateTime ObservedAtUtc);
public sealed record UsageProfile(string Name, string Description, IReadOnlyList<string> Steps, bool MakesChanges);
