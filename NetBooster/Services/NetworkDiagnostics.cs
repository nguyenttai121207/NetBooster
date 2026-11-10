using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace NetBooster.Services;

public sealed class NetworkDiagnostics
{
    private readonly ProcessRunner _runner;
    public NetworkDiagnostics(ProcessRunner runner) => _runner = runner;

    public static IPAddress? GetDefaultGateway() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().GatewayAddresses).Select(g => g.Address)
        .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

    public async Task<PingSummary> PingAsync(string target, int count = 8)
    {
        var times = new List<long>(); int received = 0;
        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            try { var reply = await ping.SendPingAsync(target, 1500); if (reply.Status == IPStatus.Success) { received++; times.Add(reply.RoundtripTime); } }
            catch { /* a failed request is represented in the result */ }
        }
        return MeasurementLogic.PingSummary(target, count, received, times);
    }

    public async Task<double> ResolveCurrentDnsMsAsync()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { await Dns.GetHostAddressesAsync("www.microsoft.com"); return watch.Elapsed.TotalMilliseconds; }
        catch { return -1; }
    }

    public async Task<WifiInfo?> GetWifiAsync()
    {
        var r = await _runner.RunAsync("netsh.exe", "wlan show interfaces");
        return r.ExitCode == 0 ? ParseNetshInterface(r.StdOut) : null;
    }
    public static WifiInfo? ParseNetshInterface(string output)
    {
        if (output.Contains("There is no wireless interface", StringComparison.OrdinalIgnoreCase) || output.Contains("There is no wireless interface on the system", StringComparison.OrdinalIgnoreCase)) return null;
        string Field(string name) => Regex.Match(output, $@"(?mi)^\s*{Regex.Escape(name)}\s*:\s*(.+)$").Groups[1].Value.Trim();
        var ssid = Field("SSID");
        return string.IsNullOrWhiteSpace(ssid) ? null : new WifiInfo(ssid, Field("Signal"), Field("Radio type"), Field("Channel"), Field("Receive rate (Mbps)"), Field("Transmit rate (Mbps)"));
    }
}

public static class MeasurementLogic
{
    public static PingSummary PingSummary(string target, int sent, int received, IReadOnlyList<long> values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        double median = sorted.Length == 0 ? -1 : sorted[sorted.Length / 2];
        double jitter = sorted.Length < 2 ? 0 : Enumerable.Range(1, sorted.Length - 1).Average(i => Math.Abs(sorted[i] - sorted[i - 1]));
        return new PingSummary(target, sent, received, (sent - received) * 100.0 / sent, median, jitter, received == 0 ? "Không nhận được phản hồi" : null);
    }
    public static string Conclusion(PingSummary? gateway, PingSummary internet, double dnsMs, WifiInfo? wifi)
    {
        if (gateway is not null && (gateway.LossPercent > 5 || gateway.JitterMs > 20)) return "Có dấu hiệu Wi‑Fi/mạng nội bộ không ổn định. Hãy đứng gần router, thử băng tần 5 GHz hoặc dây mạng.";
        if (internet.LossPercent > 5 || internet.MedianMs > 150) return "Kết nối ra Internet có mất gói hoặc độ trễ cao. Nếu gateway ổn, nguyên nhân có thể ở đường truyền/nhà mạng.";
        if (dnsMs < 0 || dnsMs > 250) return "Phân giải DNS đang chậm hoặc lỗi. Hãy chạy đo DNS trước khi quyết định đổi.";
        if (wifi is not null && wifi.Signal.EndsWith("%") && int.TryParse(wifi.Signal.TrimEnd('%'), out var s) && s < 55) return "Tín hiệu Wi‑Fi yếu; đổi DNS sẽ không cải thiện tín hiệu. Hãy cải thiện vị trí hoặc điểm phát.";
        return "Các phép đo hiện tại chưa cho thấy lỗi rõ ràng. Công cụ không thể làm tốc độ vượt gói cước của bạn.";
    }
}
