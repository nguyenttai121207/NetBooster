using NetBooster.Logic;

namespace NetBooster.Services;

/// <summary>
/// Orchestrates full system network scanning across all modules with complete fault isolation:
/// Each module runs independently. An error in one module reports "Không hỗ trợ trên máy này"
/// and will never crash or abort other modules.
/// </summary>
public sealed class ScanOrchestrator
{
    private readonly NetworkDiagnostics _diagnostics;
    private readonly WifiService _wifiService;
    private readonly DnsService _dnsService;
    private readonly BufferbloatService _bufferbloatService;
    private readonly NetworkUsageService _usageService;
    private readonly DeliveryOptimizationService _doService;
    private readonly WifiPowerService _wifiPowerService;
    private readonly PowerPlanService _powerPlanService;
    private readonly AppLogger _log;

    public ScanOrchestrator(
        NetworkDiagnostics diagnostics,
        WifiService wifiService,
        DnsService dnsService,
        BufferbloatService bufferbloatService,
        NetworkUsageService usageService,
        DeliveryOptimizationService doService,
        WifiPowerService wifiPowerService,
        PowerPlanService powerPlanService,
        AppLogger log)
    {
        _diagnostics = diagnostics;
        _wifiService = wifiService;
        _dnsService = dnsService;
        _bufferbloatService = bufferbloatService;
        _usageService = usageService;
        _doService = doService;
        _wifiPowerService = wifiPowerService;
        _powerPlanService = powerPlanService;
        _log = log;
    }

    public sealed class ScanProgress
    {
        public int Percent { get; set; }
        public string Step { get; set; } = "";
    }

    public async Task<(IReadOnlyList<CardResult> Cards, IReadOnlyList<OptimizationItem> Recommendations, ScanResult FullResult)> RunFullScanAsync(
        string loadUrl, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var cards = new List<CardResult>();
        var recommendations = new List<OptimizationItem>();

        // 1. Adapter & Wi-Fi module (0-15%)
        progress?.Report(new ScanProgress { Percent = 5, Step = "Đang kiểm tra card mạng Wi-Fi..." });
        NetworkAdapterInfo? activeAdapter = null;
        WifiInfo wifi = WifiInfo.Unknown;
        AdapterPowerState? powerState = null;
        IReadOnlyList<WifiDriverProperty> driverProps = Array.Empty<WifiDriverProperty>();

        try
        {
            var adapters = _wifiService.GetAdapters();
            activeAdapter = adapters.FirstOrDefault(a => a.IsWifi && a.IsUp) ?? adapters.FirstOrDefault(a => a.IsUp);
            if (activeAdapter != null && activeAdapter.IsWifi)
            {
                wifi = await _wifiService.GetCurrentAsync(activeAdapter);
                try { powerState = await _wifiPowerService.GetPowerStateAsync(activeAdapter); }
                catch { /* fault isolated */ }
                try { driverProps = await _wifiPowerService.GetAdvancedPropertiesAsync(activeAdapter); }
                catch { /* fault isolated */ }
            }
        }
        catch (Exception ex)
        {
            _log.Info($"Module Wi-Fi không hỗ trợ trên máy này: {ex.Message}");
        }
        cancellationToken.ThrowIfCancellationRequested();

        // 2. Gateway, Internet ping & DNS speed (15-30%)
        progress?.Report(new ScanProgress { Percent = 15, Step = "Đang đo độ trễ Gateway và Internet..." });
        PingSummary? gatewayPing = null;
        PingSummary? internetPing = null;
        double dnsMs = -1;

        try
        {
            var gatewayIp = NetworkDiagnostics.GetDefaultGateway();
            Task<PingSummary?> gwTask = gatewayIp != null
                ? _diagnostics.PingAsync(gatewayIp.ToString(), count: 6, spacingMs: 120).ContinueWith(t => (PingSummary?)t.Result)
                : Task.FromResult<PingSummary?>(null);
            var inetTask = _diagnostics.PingAsync("1.1.1.1", count: 6, spacingMs: 120);
            var dnsTask = _diagnostics.ResolveCurrentDnsMsAsync();

            await Task.WhenAll(new Task[] { gwTask, inetTask, dnsTask });
            gatewayPing = await gwTask;
            internetPing = await inetTask;
            dnsMs = await dnsTask;
        }
        catch (Exception ex)
        {
            _log.Info($"Module Ping không hỗ trợ hoặc lỗi: {ex.Message}");
        }
        cancellationToken.ThrowIfCancellationRequested();

        // 3. Bufferbloat measurement (30-60%)
        progress?.Report(new ScanProgress { Percent = 30, Step = "Đang đo Bufferbloat dưới tải..." });
        BufferbloatResult? bufferbloat = null;
        try
        {
            bufferbloat = await _bufferbloatService.MeasureAsync(loadUrl, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.Info($"Module Bufferbloat không hoàn tất được: {ex.Message}");
        }
        cancellationToken.ThrowIfCancellationRequested();

        // 4. Network process hogs (60-70%)
        progress?.Report(new ScanProgress { Percent = 60, Step = "Đang quét tiến trình mạng..." });
        IReadOnlyList<NetworkProcessActivity> processes = Array.Empty<NetworkProcessActivity>();
        try
        {
            processes = await _usageService.GetActiveTcpProcessesAsync();
        }
        catch (Exception ex)
        {
            _log.Info($"Module Network Process Hogs lỗi: {ex.Message}");
        }
        cancellationToken.ThrowIfCancellationRequested();

        // 5. Delivery Optimization (70-75%)
        progress?.Report(new ScanProgress { Percent = 70, Step = "Đang kiểm tra Windows Delivery Optimization..." });
        DeliveryOptimizationState? doState = null;
        try
        {
            doState = await _doService.GetStateAsync();
        }
        catch (Exception ex)
        {
            _log.Info($"Module Delivery Optimization không hỗ trợ trên máy này: {ex.Message}");
            doState = new DeliveryOptimizationState(false, null, "Không hỗ trợ trên máy này");
        }
        cancellationToken.ThrowIfCancellationRequested();

        // 6. Wi-Fi Channel Scanner (75-85%)
        progress?.Report(new ScanProgress { Percent = 78, Step = "Đang quét các kênh Wi-Fi xung quanh..." });
        IReadOnlyList<WifiBss> bssList = Array.Empty<WifiBss>();
        IReadOnlyList<ChannelRecommendation> channelRecs = Array.Empty<ChannelRecommendation>();
        if (activeAdapter != null && activeAdapter.IsWifi)
        {
            try
            {
                bssList = await _wifiService.ScanAsync(activeAdapter, cancellationToken);
                channelRecs = WifiChannelLogic.Recommend(bssList);
            }
            catch (Exception ex)
            {
                _log.Info($"Module Wi-Fi Scanner không hỗ trợ hoặc không có sóng: {ex.Message}");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();

        // 7. Power Plan status (85-92%)
        progress?.Report(new ScanProgress { Percent = 85, Step = "Đang kiểm tra chế độ nguồn Windows Power Plan..." });
        bool isHighPerf = false;
        bool onBattery = PowerPlanService.IsRunningOnBattery();
        try
        {
            isHighPerf = await _powerPlanService.IsHighPerformanceActiveAsync();
        }
        catch { /* fault isolated */ }

        progress?.Report(new ScanProgress { Percent = 95, Step = "Đang tổng hợp kết quả chẩn đoán..." });

        // === BUILD STATUS CARDS ===

        // CARD 1: Độ ổn định & Bufferbloat
        var stabilityStatus = StatusLevel.Good;
        var stabilitySummary = "Mạng ổn định";
        var stabilityDetails = $"• Gateway nội bộ: {FormatPing(gatewayPing)}\n• Ra Internet (1.1.1.1): {FormatPing(internetPing)}";

        if (bufferbloat != null)
        {
            stabilitySummary = bufferbloat.Conclusion;
            stabilityDetails += $"\n• Kết quả Bufferbloat: {bufferbloat.Conclusion}";
            if (bufferbloat.Gateway != null)
                stabilityDetails += $"\n  - Tải Gateway: {bufferbloat.Gateway.Loaded.MedianMs:F0} ms (tăng {bufferbloat.Gateway.MedianIncreaseMs:+0;-0;0} ms, jitter {bufferbloat.Gateway.Loaded.JitterMs:F0} ms)";
            stabilityDetails += $"\n  - Tải Internet: {bufferbloat.Internet.Loaded.MedianMs:F0} ms (tăng {bufferbloat.Internet.MedianIncreaseMs:+0;-0;0} ms, jitter {bufferbloat.Internet.Loaded.JitterMs:F0} ms)";
            stabilityDetails += $"\n  - Dữ liệu đã tải thử nghiệm: {bufferbloat.DownloadedBytes / (1024 * 1024):F1} MB";

            if (bufferbloat.Conclusion.Contains("Bufferbloat cao", StringComparison.OrdinalIgnoreCase))
            {
                stabilityStatus = StatusLevel.Warning;
            }
            else if (bufferbloat.Conclusion.Contains("chập chờn", StringComparison.OrdinalIgnoreCase) ||
                     bufferbloat.Conclusion.Contains("mất gói", StringComparison.OrdinalIgnoreCase))
            {
                stabilityStatus = StatusLevel.Bad;
            }
        }
        else
        {
            if (gatewayPing != null && (gatewayPing.LossPercent > 5 || gatewayPing.JitterMs > 20))
            {
                stabilityStatus = StatusLevel.Bad;
                stabilitySummary = "Wi-Fi chập chờn, mất gói";
            }
            else if (internetPing != null && (internetPing.LossPercent > 5 || internetPing.MedianMs > 150))
            {
                stabilityStatus = StatusLevel.Warning;
                stabilitySummary = "Độ trễ Internet cao hoặc mất gói";
            }
        }
        cards.Add(new CardResult("Độ ổn định & Bufferbloat", stabilitySummary, stabilityStatus, stabilityDetails));

        // CARD 2: Tối ưu Card Wi-Fi
        if (wifi.IsConnected)
        {
            var wifiStatus = StatusLevel.Good;
            var wifiSummary = $"{wifi.Ssid} • Băng tần {wifi.Band} • Tín hiệu {wifi.SignalPercent}%";
            var wifiDetails = $"• Tên mạng (SSID): {wifi.Ssid}\n• Địa chỉ MAC (BSSID): {wifi.Bssid}\n• Băng tần: {wifi.Band}\n• Chuẩn Wi-Fi: {wifi.RadioType}\n" +
                              $"• Cường độ sóng: {wifi.SignalPercent}%\n• Kênh phát: {wifi.Channel}\n" +
                              $"• Tốc độ liên kết: Nhận {wifi.ReceiveMbps:F0} Mbps / Gửi {wifi.TransmitMbps:F0} Mbps";

            if (powerState != null)
            {
                wifiDetails += $"\n\n[Trạng thái Tiết kiệm điện]:\n  - SelectiveSuspend: {powerState.SelectiveSuspend ?? "N/A"}\n  - DeviceSleepOnDisconnect: {powerState.DeviceSleepOnDisconnect ?? "N/A"}\n  - D0PacketCoalescing: {powerState.D0PacketCoalescing ?? "N/A"}";
            }
            if (driverProps.Count > 0)
            {
                wifiDetails += "\n\n[Thuộc tính nâng cao Driver]:\n" + string.Join("\n", driverProps.Select(p => $"  - {p.DisplayName}: {p.CurrentValue}"));
            }

            if (wifi.SignalPercent < 45)
            {
                wifiStatus = StatusLevel.Bad;
                wifiSummary = $"{wifi.Ssid} • Sóng rất yếu ({wifi.SignalPercent}%)";
            }
            else if (wifi.SignalPercent < 65)
            {
                wifiStatus = StatusLevel.Warning;
                wifiSummary = $"{wifi.Ssid} • Sóng trung bình ({wifi.SignalPercent}%)";
            }

            if (channelRecs.Count > 0)
            {
                wifiDetails += "\n\n[Gợi ý chọn kênh Router]:\n" + string.Join("\n", channelRecs.Select(r => $"  - {r.Band}: Kênh {r.Channel} ({r.NearbyNetworks} mạng lân cận). {r.Advice}"));
            }
            cards.Add(new CardResult("Card Wi-Fi & Sóng", wifiSummary, wifiStatus, wifiDetails));
        }
        else
        {
            var summary = activeAdapter != null ? $"Đang dùng {activeAdapter.Name} ({activeAdapter.Description})" : "Không kết nối Wi-Fi";
            cards.Add(new CardResult("Card Wi-Fi & Sóng", summary, StatusLevel.Good, "Thiết bị đang sử dụng kết nối dây (Ethernet) hoặc chưa kết nối Wi-Fi."));
        }

        // CARD 3: DNS
        var dnsStatus = dnsMs < 0 ? StatusLevel.Bad : dnsMs > 150 ? StatusLevel.Warning : StatusLevel.Good;
        var dnsSummary = dnsMs < 0 ? "DNS không phản hồi" : $"Phản hồi trong {dnsMs:F0} ms";
        var dnsDetails = $"• Thời gian phân giải hiện tại (microsoft.com): {(dnsMs < 0 ? "Thất bại" : $"{dnsMs:F0} ms")}\n" +
                         "• Để so sánh với Cloudflare (1.1.1.1), Google (8.8.8.8) và Quad9, hãy mở rộng mục [Nâng cao > DNS Benchmark].";
        cards.Add(new CardResult("DNS & Phân giải tên miền", dnsSummary, dnsStatus, dnsDetails));

        // CARD 4: Tiến trình ngốn mạng
        var topProcesses = processes.Take(6).ToArray();
        var procSummary = topProcesses.Length == 0 ? "Không có tiến trình chiếm dụng" : $"{topProcesses.Length} tiến trình đang kết nối mạng";
        var procStatus = topProcesses.Any(p => p.ActiveTcpConnections > 40) ? StatusLevel.Warning : StatusLevel.Good;
        var procDetails = topProcesses.Length == 0 ? "Không phát hiện tiến trình nào đang mở nhiều kết nối mạng." :
            string.Join("\n", topProcesses.Select(p => $"• {p.ProcessName} (PID: {p.ProcessId}) — {p.ActiveTcpConnections} kết nối TCP"));
        cards.Add(new CardResult("Tiến trình mạng (Network Hogs)", procSummary, procStatus, procDetails));

        // === BUILD RECOMMENDATIONS ===

        // Recommendation 1: Wi-Fi Power saving
        if (activeAdapter != null && activeAdapter.IsWifi && powerState != null &&
            (powerState.SelectiveSuspend == "Enabled" || powerState.DeviceSleepOnDisconnect == "Enabled" || powerState.D0PacketCoalescing == "Enabled"))
        {
            recommendations.Add(new OptimizationItem(
                "wifi-power",
                "Wi-Fi",
                "Tắt tiết kiệm điện card Wi-Fi",
                "Tắt Selective Suspend và Packet Coalescing trên card Wi-Fi giúp tránh drop gói và lag spike bất chợt.",
                StatusLevel.Warning,
                true,
                false));
        }

        // Recommendation 2: Driver Advanced Properties
        if (activeAdapter != null && activeAdapter.IsWifi && driverProps.Count > 0)
        {
            var prefBand = driverProps.FirstOrDefault(p => p.DisplayName.Contains("Preferred Band", StringComparison.OrdinalIgnoreCase));
            if (prefBand != null && !prefBand.CurrentValue.Contains("5GHz", StringComparison.OrdinalIgnoreCase) && prefBand.ValidValues.Any(v => v.Contains("5GHz")))
            {
                recommendations.Add(new OptimizationItem(
                    "driver-band",
                    "Wi-Fi Driver",
                    "Ưu tiên băng tần 5GHz trên Driver Wi-Fi",
                    $"Driver đang đặt: '{prefBand.CurrentValue}'. Chuyển sang ưu tiên 5GHz giúp giảm nhiễu sóng kênh 2.4GHz.",
                    StatusLevel.Warning,
                    true,
                    false));
            }

            var roaming = driverProps.FirstOrDefault(p => p.DisplayName.Contains("Roaming Aggressiveness", StringComparison.OrdinalIgnoreCase));
            if (roaming != null && (roaming.CurrentValue.Contains("Medium", StringComparison.OrdinalIgnoreCase) || roaming.CurrentValue.Contains("High", StringComparison.OrdinalIgnoreCase)))
            {
                recommendations.Add(new OptimizationItem(
                    "driver-roaming",
                    "Wi-Fi Driver",
                    "Giảm mức độ Roaming Aggressiveness (tìm sóng liên tục)",
                    $"Hiện tại là '{roaming.CurrentValue}'. Giảm xuống mức Medium-low giúp card không liên tục quét ngắt quãng khi ngồi một chỗ chơi game/họp.",
                    StatusLevel.Warning,
                    true,
                    false));
            }
        }

        // Recommendation 3: Power Plan (High Performance)
        if (!isHighPerf)
        {
            recommendations.Add(new OptimizationItem(
                "power-plan",
                "Hệ thống",
                "Kích hoạt Power Plan Hiệu suất cao (High Performance)",
                onBattery
                    ? "Máy đang chạy bằng PIN. Khuyến nghị cắm sạc trước khi bật High Performance."
                    : "Đổi Power Plan sang High Performance giúp CPU và card Wi-Fi luôn ở xung nhịp phản hồi nhanh nhất.",
                StatusLevel.Warning,
                !onBattery, // Do NOT check by default when on battery
                false));
        }

        // Recommendation 4: Delivery Optimization
        if (doState != null && doState.IsSupported && (doState.BackgroundPercent is null or > 10))
        {
            recommendations.Add(new OptimizationItem(
                "do-limit",
                "Windows Update",
                "Giới hạn Delivery Optimization nền ở mức 10%",
                "Ngăn Windows Update tải ngầm và chia sẻ P2P chiếm hết băng thông đường truyền lúc đang chơi game.",
                StatusLevel.Warning,
                true,
                false));
        }

        // Recommendation 5: Channel recommendation (advisory only)
        if (channelRecs.Count > 0)
        {
            foreach (var rec in channelRecs.Where(r => r.NearbyNetworks >= 3))
            {
                recommendations.Add(new OptimizationItem(
                    $"router-channel-{rec.Band}",
                    "Router Wi-Fi",
                    $"Đổi kênh {rec.Band} trên Router sang kênh {rec.Channel}",
                    $"{rec.Advice} (Chỉ cấu hình trên trang quản trị Router, ứng dụng không can thiệp Router).",
                    StatusLevel.Warning,
                    false,
                    false));
            }
        }

        progress?.Report(new ScanProgress { Percent = 100, Step = "Hoàn tất quét mạng!" });
        _log.Info("Hoàn tất quét mạng tổng thể.");

        var fullResult = new ScanResult(
            gatewayPing,
            internetPing,
            wifi,
            dnsMs,
            processes,
            doState,
            powerState,
            driverProps,
            bssList,
            channelRecs,
            bufferbloat);

        return (cards, recommendations, fullResult);
    }

    private static string FormatPing(PingSummary? p) =>
        p == null ? "Không phản hồi" :
        $"{p.Received}/{p.Sent} gói ({p.LossPercent:F0}% mất) • median {p.MedianMs:F0} ms • jitter {p.JitterMs:F0} ms";
}
