using System.Runtime.InteropServices;
using System.Text;

namespace NetBooster.Services;

/// <summary>Small Native Wi-Fi wrapper. It intentionally returns unknown rather than parsing localized netsh text.</summary>
internal static class NativeWifi
{
    private const int ErrorSuccess = 0;

    public static WifiInfo GetCurrent(Guid interfaceGuid)
    {
        using var client = new WlanClient();
        var connection = client.GetCurrentConnection(interfaceGuid);
        if (connection is null || connection.State != WlanInterfaceState.Connected) return WifiInfo.Unknown;
        var bss = client.GetBssEntries(interfaceGuid).FirstOrDefault(x => string.Equals(x.Bssid, connection.Bssid, StringComparison.OrdinalIgnoreCase));
        var channel = bss?.Channel ?? connection.Channel;
        var band = bss?.Band ?? BandForChannelOnly(channel);
        return new WifiInfo(connection.Ssid, connection.Bssid, (int)connection.SignalQuality, PhyName(connection.PhyType), channel, band,
            connection.ReceiveRateBitsPerSecond / 1_000_000d, connection.TransmitRateBitsPerSecond / 1_000_000d, true);
    }

    public static async Task<IReadOnlyList<WifiBss>> ScanAsync(Guid interfaceGuid, CancellationToken cancellationToken)
    {
        using var client = new WlanClient();
        client.StartScan(interfaceGuid);
        // Windows scans asynchronously. We wait briefly for its cached BSS list to refresh, then read it once.
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        return client.GetBssEntries(interfaceGuid).Select(x => new WifiBss(x.Ssid, x.Bssid, x.Channel, x.Band, (int)x.SignalQuality)).ToArray();
    }

    internal static string PhyName(uint phy) => phy switch
    {
        1 => "FHSS", 2 => "DSSS", 3 => "IR baseband", 4 => "OFDM", 5 => "HR-DSSS", 6 => "ERP", 7 => "802.11n (HT)",
        8 => "802.11ac (VHT)", 9 => "802.11ad (DMG)", 10 => "802.11ax (HE)", 11 => "802.11be (EHT)", _ => "Không xác định"
    };

    internal static (int? Channel, string Band) ChannelFromFrequencyKHz(uint frequencyKHz)
    {
        var mhz = frequencyKHz / 1000d;
        if (mhz >= 2400 && mhz <= 2500)
        {
            var channel = Math.Abs(mhz - 2484) < 1 ? 14 : (int)Math.Round((mhz - 2407) / 5);
            return channel is >= 1 and <= 14 ? (channel, "2.4 GHz") : (null, "Không xác định");
        }
        if (mhz >= 4900 && mhz <= 5900) return ((int)Math.Round((mhz - 5000) / 5), "5 GHz");
        if (mhz >= 5925 && mhz <= 7125) return ((int)Math.Round((mhz - 5950) / 5), "6 GHz");
        return (null, "Không xác định");
    }

    private static string BandForChannelOnly(int? channel) => channel is >= 1 and <= 14 ? "2.4 GHz" : "Không xác định";

    private sealed class WlanClient : IDisposable
    {
        private IntPtr _handle;
        public WlanClient()
        {
            var result = WlanOpenHandle(2, IntPtr.Zero, out _, out _handle);
            if (result != ErrorSuccess) throw new InvalidOperationException($"Native Wi‑Fi API không khả dụng (mã {result}).");
        }
        public void Dispose() { if (_handle != IntPtr.Zero) { WlanCloseHandle(_handle, IntPtr.Zero); _handle = IntPtr.Zero; } }
        public void StartScan(Guid interfaceGuid)
        {
            var result = WlanScan(_handle, ref interfaceGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (result != ErrorSuccess) throw new InvalidOperationException($"Không thể quét Wi‑Fi (mã {result}).");
        }
        public Connection? GetCurrentConnection(Guid interfaceGuid)
        {
            var opcode = WlanIntfOpcode.CurrentConnection;
            var result = WlanQueryInterface(_handle, ref interfaceGuid, opcode, IntPtr.Zero, out var size, out var data, out _);
            if (result != ErrorSuccess || data == IntPtr.Zero) return null;
            try
            {
                var attributes = Marshal.PtrToStructure<WlanConnectionAttributes>(data);
                var channel = QueryChannel(interfaceGuid);
                return new Connection(attributes.IsState, Ssid(attributes.Association.Ssid), Bssid(attributes.Association.Bssid), attributes.Association.SignalQuality, attributes.Association.PhyType, attributes.Association.RxRate, attributes.Association.TxRate, channel);
            }
            finally { WlanFreeMemory(data); }
        }
        private int? QueryChannel(Guid interfaceGuid)
        {
            var result = WlanQueryInterface(_handle, ref interfaceGuid, WlanIntfOpcode.ChannelNumber, IntPtr.Zero, out var size, out var data, out _);
            if (result != ErrorSuccess || data == IntPtr.Zero || size < sizeof(uint)) return null;
            try { return checked((int)(uint)Marshal.ReadInt32(data)); }
            finally { WlanFreeMemory(data); }
        }
        public IReadOnlyList<BssEntry> GetBssEntries(Guid interfaceGuid)
        {
            var result = WlanGetNetworkBssList(_handle, ref interfaceGuid, IntPtr.Zero, Dot11BssType.Any, false, IntPtr.Zero, out var list);
            if (result != ErrorSuccess || list == IntPtr.Zero) return Array.Empty<BssEntry>();
            try
            {
                var count = Marshal.ReadInt32(list);
                var itemSize = Marshal.SizeOf<WlanBssEntry>();
                var entries = new List<BssEntry>(Math.Max(0, count));
                for (var i = 0; i < count; i++)
                {
                    var ptr = IntPtr.Add(list, 8 + i * itemSize);
                    var e = Marshal.PtrToStructure<WlanBssEntry>(ptr);
                    var (channel, band) = ChannelFromFrequencyKHz(e.CenterFrequencyKHz);
                    entries.Add(new BssEntry(Ssid(e.Ssid), Bssid(e.Bssid), channel, band, e.LinkQuality));
                }
                return entries;
            }
            finally { WlanFreeMemory(list); }
        }
    }

    private sealed record Connection(WlanInterfaceState State, string Ssid, string Bssid, uint SignalQuality, uint PhyType, uint ReceiveRateBitsPerSecond, uint TransmitRateBitsPerSecond, int? Channel);
    private sealed record BssEntry(string Ssid, string Bssid, int? Channel, string Band, uint SignalQuality);
    private static string Ssid(Dot11Ssid ssid) => ssid.Length == 0 || ssid.Value is null ? "Không xác định" : Encoding.UTF8.GetString(ssid.Value, 0, checked((int)Math.Min(ssid.Length, 32)));
    private static string Bssid(byte[]? bssid) => bssid is { Length: 6 } ? string.Join(':', bssid.Select(x => x.ToString("X2"))) : "Không xác định";

    private enum WlanInterfaceState { NotReady = 0, Connected = 1, AdHocNetworkFormed = 2, Disconnecting = 3, Disconnected = 4, Associating = 5, Discovering = 6, Authenticating = 7 }
    private enum WlanIntfOpcode { InterfaceState = 6, CurrentConnection = 7, ChannelNumber = 8 }
    private enum Dot11BssType { Infrastructure = 1, Independent = 2, Any = 3 }

    [StructLayout(LayoutKind.Sequential)] private struct Dot11Ssid { public uint Length; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Value; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WlanConnectionAttributes
    {
        public WlanInterfaceState IsState; public int ConnectionMode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public WlanAssociationAttributes Association; public WlanSecurityAttributes Security;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WlanAssociationAttributes
    {
        public Dot11Ssid Ssid; public Dot11BssType BssType; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
        public uint PhyType; public uint PhyIndex; public uint SignalQuality; public uint RxRate; public uint TxRate;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WlanSecurityAttributes { [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled; [MarshalAs(UnmanagedType.Bool)] public bool OneXEnabled; public int AuthAlgorithm; public int CipherAlgorithm; }
    [StructLayout(LayoutKind.Sequential)] private struct WlanBssEntry
    {
        public uint PhyId; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid; public Dot11BssType BssType; public uint PhyType;
        public int Rssi; public uint LinkQuality; public byte InRegDomain; public ushort BeaconPeriod; public ulong Timestamp; public ulong HostTimestamp;
        public ushort CapabilityInformation; public uint CenterFrequencyKHz; public WlanRateSet RateSet;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WlanRateSet { public uint Length; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)] public ushort[] Rates; }

    [DllImport("wlanapi.dll", SetLastError = true)] private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);
    [DllImport("wlanapi.dll", SetLastError = true)] private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);
    [DllImport("wlanapi.dll", SetLastError = true)] private static extern void WlanFreeMemory(IntPtr memory);
    [DllImport("wlanapi.dll", SetLastError = true)] private static extern uint WlanScan(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr ssid, IntPtr ieData, IntPtr reserved);
    [DllImport("wlanapi.dll", SetLastError = true)] private static extern uint WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, WlanIntfOpcode opcode, IntPtr reserved, out uint dataSize, out IntPtr data, out int valueType);
    [DllImport("wlanapi.dll", SetLastError = true)] private static extern uint WlanGetNetworkBssList(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr dot11Ssid, Dot11BssType bssType, [MarshalAs(UnmanagedType.Bool)] bool securityEnabled, IntPtr reserved, out IntPtr wlanBssList);
}
