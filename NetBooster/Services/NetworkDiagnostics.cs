using System.Net;
using System.Net.NetworkInformation;

namespace NetBooster.Services;

public sealed class NetworkDiagnostics
{
    public static IPAddress? GetDefaultGateway() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().GatewayAddresses)
        .Select(g => g.Address)
        .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

    public async Task<PingSummary> PingAsync(string target, int count = 8, int timeoutMs = 1500, int spacingMs = 150)
    {
        if (!IPAddress.TryParse(target, out var address)) return new PingSummary(target, 0, 0, 100, -1, -1, "Địa chỉ IP không hợp lệ.");
        var times = new List<long>();
        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(address, timeoutMs);
                if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
            }
            catch { /* represented as a failed sample */ }
            if (i + 1 < count) await Task.Delay(spacingMs);
        }
        return MeasurementLogic.BuildPingSummary(target, count, times);
    }

    public async Task<double> ResolveCurrentDnsMsAsync()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { await Dns.GetHostAddressesAsync("www.microsoft.com"); return watch.Elapsed.TotalMilliseconds; }
        catch { return -1; }
    }
}

public static class MeasurementLogic
{
    public static PingSummary BuildPingSummary(string target, int sent, IReadOnlyList<long> orderedRoundTrips)
    {
        var sorted = orderedRoundTrips.OrderBy(x => x).ToArray();
        var received = orderedRoundTrips.Count;
        var median = sorted.Length == 0 ? -1 : sorted[sorted.Length / 2];
        var jitter = orderedRoundTrips.Count < 2 ? 0 : Enumerable.Range(1, orderedRoundTrips.Count - 1).Average(i => Math.Abs(orderedRoundTrips[i] - orderedRoundTrips[i - 1]));
        return new PingSummary(target, sent, received, sent <= 0 ? 100 : (sent - received) * 100.0 / sent, median, jitter, received == 0 ? "Không nhận được phản hồi" : null);
    }

    public static string Diagnose(PingSummary? gateway, PingSummary internet, double dnsMs, WifiInfo wifi)
    {
        if (gateway is not null && (gateway.LossPercent > 5 || gateway.JitterMs > 20)) return "Có dấu hiệu Wi‑Fi/mạng nội bộ không ổn định. Hãy kiểm tra sóng, vị trí router, băng tần hoặc thử dây mạng.";
        if (internet.LossPercent > 5 || internet.MedianMs > 150) return "Kết nối ra Internet có mất gói hoặc độ trễ cao. Nếu gateway ổn, nguyên nhân có thể ở đường truyền hoặc nhà mạng.";
        if (dnsMs < 0 || dnsMs > 250) return "Phân giải DNS đang chậm hoặc lỗi. Hãy chạy đo DNS trước khi quyết định đổi.";
        if (wifi.SignalPercent is < 55) return "Tín hiệu Wi‑Fi yếu; đổi DNS sẽ không cải thiện sóng. Hãy cải thiện vị trí hoặc điểm phát.";
        return "Các phép đo hiện tại chưa cho thấy lỗi rõ ràng. NetBooster không thể làm tốc độ vượt gói cước.";
    }
}
