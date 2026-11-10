using NetBooster.Services;

namespace NetBooster.UI;

public sealed class MainForm : Form
{
    private readonly AppLogger _log = new();
    private readonly NetworkDiagnostics _diagnostics;
    private readonly DnsService _dns;
    private readonly WireGuardService _wireGuard;
    private readonly TextBox _output = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9) };
    private readonly ComboBox _adapter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly CheckBox _doh = new() { Text = "Thử bật DNS-over-HTTPS (nếu Windows hỗ trợ)" };
    private readonly TextBox _configPath = new() { Width = 420, ReadOnly = true };
    private string? _selectedConfig;

    public MainForm()
    {
        Text = "NetBooster — chẩn đoán mạng trung thực"; Width = 820; Height = 610; MinimumSize = new Size(700, 480);
        var runner = new ProcessRunner(); _diagnostics = new NetworkDiagnostics(runner); _dns = new DnsService(runner, _log); _wireGuard = new WireGuardService(runner, _log);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(DiagnosticsTab()); tabs.TabPages.Add(DnsTab()); tabs.TabPages.Add(VpnTab()); tabs.TabPages.Add(LogTab());
        _output.Dock = DockStyle.Bottom; _output.Height = 165;
        Controls.Add(_output); Controls.Add(tabs);
        Shown += async (_, _) => await LoadAdaptersAsync();
    }
    private TabPage DiagnosticsTab()
    {
        var page = new TabPage("Chẩn đoán"); var panel = Panel();
        panel.Controls.Add(Note("NetBooster chỉ đo và gợi ý; không thể làm tốc độ Internet vượt gói cước."));
        panel.Controls.Add(Button("Chạy chẩn đoán", async () => await DiagnoseAsync()));
        page.Controls.Add(panel); return page;
    }
    private TabPage DnsTab()
    {
        var page = new TabPage("DNS"); var p = Panel();
        p.Controls.Add(Note("Chỉ đổi DNS khi phép đo cho thấy cải thiện đáng kể. Mọi thao tác đổi/khôi phục cần quyền Quản trị viên."));
        p.Controls.Add(new Label { Text = "Card mạng đang dùng:", AutoSize = true }); p.Controls.Add(_adapter);
        p.Controls.Add(Button("Đo DNS (hiện tại, Cloudflare, Google, Quad9)", async () => await BenchmarkDnsAsync()));
        p.Controls.Add(_doh);
        p.Controls.Add(Button("Áp dụng Cloudflare (1.1.1.1, 1.0.0.1)", async () => await ApplyDnsAsync(new[] { "1.1.1.1", "1.0.0.1" })));
        p.Controls.Add(Button("Khôi phục DNS đã sao lưu", async () => await RestoreDnsAsync()));
        p.Controls.Add(Button("Xóa cache DNS", async () => await ClearCacheAsync())); page.Controls.Add(p); return page;
    }
    private TabPage VpnTab()
    {
        var page = new TabPage("VPN WireGuard"); var p = Panel();
        p.Controls.Add(Note("Chỉ dùng WireGuard chính thức và tệp .conf của bạn. NetBooster không lưu hoặc ghi log private key."));
        p.Controls.Add(new Label { Text = "Trạng thái: chọn tệp .conf để kiểm tra. Nếu chưa có WireGuard, cài từ wireguard.com/install.", AutoSize = true, MaximumSize = new Size(700, 0) });
        p.Controls.Add(_configPath); p.Controls.Add(Button("Chọn .conf", ChooseConfig));
        p.Controls.Add(Button("Bật tunnel", async () => await EnableVpnAsync()));
        p.Controls.Add(Button("Tắt tunnel đã chọn", async () => await DisableVpnAsync()));
        p.Controls.Add(Button("Kiểm tra IP công khai (gọi api.ipify.org)", async () => await PublicIpAsync()));
        p.Controls.Add(Button("Kiểm tra DNS sau VPN", async () => await LeakCheckAsync())); page.Controls.Add(p); return page;
    }
    private TabPage LogTab()
    {
        var page = new TabPage("Nhật ký"); var p = Panel();
        p.Controls.Add(Note("Nhật ký chỉ ghi hành động và lỗi; không chứa cấu hình hoặc khóa WireGuard."));
        p.Controls.Add(Button("Tải lại nhật ký", () => _output.Text = _log.ReadAll())); page.Controls.Add(p); return page;
    }
    private static FlowLayoutPanel Panel() => new() { Dock = DockStyle.Fill, Padding = new Padding(12), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private static Label Note(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(730, 0), ForeColor = Color.DarkSlateGray, Margin = new Padding(3, 3, 3, 12) };
    private static Button Button(string text, Action action) { var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(3, 5, 3, 5) }; b.Click += (_, _) => action(); return b; }
    private async Task LoadAdaptersAsync()
    {
        try { _adapter.Items.Clear(); _adapter.Items.AddRange((await _dns.GetActiveAdaptersAsync()).Cast<object>().ToArray()); if (_adapter.Items.Count > 0) _adapter.SelectedIndex = 0; }
        catch (Exception ex) { ShowError(ex); }
    }
    private async Task DiagnoseAsync()
    {
        await RunUiAsync(async () =>
        {
            Write("Đang đo gateway, Internet và DNS…");
            var gatewayIp = NetworkDiagnostics.GetDefaultGateway();
            var gateway = gatewayIp is null ? null : await _diagnostics.PingAsync(gatewayIp.ToString());
            var internet = await _diagnostics.PingAsync("1.1.1.1"); var dns = await _diagnostics.ResolveCurrentDnsMsAsync(); var wifi = await _diagnostics.GetWifiAsync();
            Write($"Gateway: {Format(gateway)}\r\nInternet (1.1.1.1): {Format(internet)}\r\nDNS hiện tại: {(dns < 0 ? "lỗi" : $"{dns:F0} ms")}");
            if (wifi is not null) Write($"Wi‑Fi: SSID {wifi.Ssid}; sóng {wifi.Signal}; {wifi.RadioType}; kênh {wifi.Channel}; nhận/gửi {wifi.ReceiveRate}/{wifi.TransmitRate} Mbps");
            else Write("Wi‑Fi: không phát hiện kết nối Wi‑Fi đang hoạt động.");
            Write("Kết luận: " + MeasurementLogic.Conclusion(gateway, internet, dns, wifi)); _log.Info("Đã chạy chẩn đoán mạng.");
        });
    }
    private async Task BenchmarkDnsAsync()
    {
        if (!RequireAdapter()) return;
        await RunUiAsync(async () =>
        {
            var current = await _dns.GetServersAsync(_adapter.Text);
            var candidates = current.Concat(new[] { "1.1.1.1", "8.8.8.8", "9.9.9.9" }).Distinct().ToArray();
            var domains = new[] { "www.microsoft.com", "www.cloudflare.com", "www.wikipedia.org" };
            Write("Đang đo 3 tên miền × 3 lần trên từng DNS…");
            var results = new List<DnsMeasurement>(); foreach (var s in candidates) { var x = await _dns.MeasureAsync(s, domains); results.Add(x); Write($"{x.Server}: trung vị {x.MedianMs:F0} ms, lỗi {x.FailurePercent:F0}% ({x.Samples} mẫu)"); }
            var baseline = results.Where(x => current.Contains(x.Server)).OrderBy(x => x.MedianMs < 0 ? double.MaxValue : x.MedianMs).FirstOrDefault(); var best = results.Where(x => x.MedianMs >= 0).OrderBy(x => x.MedianMs).FirstOrDefault();
            if (baseline is not null && best is not null && best.Server != baseline.Server && best.FailurePercent <= baseline.FailurePercent && best.MedianMs <= baseline.MedianMs * .8) Write($"Đề xuất: {best.Server} nhanh hơn ít nhất 20% so với DNS hiện tại. Bạn có thể áp dụng DNS đã chọn bên dưới (bản đầu chỉ có preset Cloudflare).");
            else Write("Kết luận: DNS hiện tại đã tốt, hoặc chưa đủ bằng chứng cải thiện trên 20%; không khuyến nghị đổi.");
            _log.Info("Đã đo benchmark DNS.");
        });
    }
    private async Task ApplyDnsAsync(string[] servers)
    {
        if (!RequireAdapter() || !EnsureAdmin()) return;
        if (MessageBox.Show($"Đổi DNS của '{_adapter.Text}' sang {string.Join(", ", servers)}?\n\nCấu hình hiện tại sẽ được sao lưu và tự khôi phục nếu lỗi.", "Xác nhận đổi DNS", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await RunUiAsync(async () => { await _dns.ApplyAsync(_adapter.Text, servers, _doh.Checked); Write("Đã đổi DNS thành công. Dùng nút Khôi phục để quay lại cấu hình đã sao lưu."); });
    }
    private async Task RestoreDnsAsync()
    {
        if (!EnsureAdmin()) return;
        if (MessageBox.Show("Khôi phục DNS từ bản sao lưu gần nhất?", "Xác nhận khôi phục", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await RunUiAsync(async () => { await _dns.RestoreSavedAsync(); Write("Đã khôi phục DNS."); });
    }
    private async Task ClearCacheAsync() { if (!EnsureAdmin()) return; await RunUiAsync(async () => { await _dns.ClearCacheAsync(); Write("Đã xóa cache DNS."); }); }
    private void ChooseConfig()
    {
        using var dialog = new OpenFileDialog { Filter = "WireGuard configuration (*.conf)|*.conf", CheckFileExists = true };
        if (dialog.ShowDialog() != DialogResult.OK) return; var error = WireGuardService.ValidateConfig(dialog.FileName);
        if (error.Length > 0) { MessageBox.Show(error, "Tệp không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        _selectedConfig = dialog.FileName; _configPath.Text = dialog.FileName; Write("Tệp cấu hình hợp lệ. Nội dung tệp không được hiển thị hoặc ghi nhật ký.");
    }
    private async Task EnableVpnAsync()
    {
        if (_selectedConfig is null) { MessageBox.Show("Hãy chọn tệp .conf trước."); return; } if (!EnsureAdmin()) return;
        if (MessageBox.Show("Bật WireGuard với tệp bạn đã chọn?", "Xác nhận VPN", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await RunUiAsync(async () => { await _wireGuard.EnableAsync(_selectedConfig); Write("Đã yêu cầu WireGuard bật tunnel. Có thể mất vài giây để trạng thái mạng cập nhật."); });
    }
    private async Task DisableVpnAsync()
    {
        if (_selectedConfig is null) { MessageBox.Show("Hãy chọn đúng tệp .conf của tunnel cần tắt."); return; } if (!EnsureAdmin()) return;
        var name = Path.GetFileNameWithoutExtension(_selectedConfig);
        if (MessageBox.Show($"Gỡ tunnel service '{name}'?", "Xác nhận tắt VPN", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await RunUiAsync(async () => { await _wireGuard.DisableAsync(name); Write("Đã yêu cầu tắt tunnel WireGuard."); });
    }
    private async Task PublicIpAsync() => await RunUiAsync(async () => Write("IP công khai: " + await _wireGuard.PublicIpAsync()));
    private async Task LeakCheckAsync() => await RunUiAsync(async () => Write((await _wireGuard.DnsLeakCheckAsync()) ? "Phân giải DNS kiểm tra hoạt động. Đây không phải kiểm toán rò rỉ DNS hoàn chỉnh." : "Không phân giải được DNS kiểm tra; hãy kiểm tra trạng thái VPN/DNS."));
    private bool RequireAdapter() { if (_adapter.SelectedItem is not null) return true; MessageBox.Show("Không tìm thấy card mạng đang hoạt động."); return false; }
    private bool EnsureAdmin()
    {
        if (DnsService.IsAdministrator()) return true;
        if (MessageBox.Show("Thao tác này cần quyền Quản trị viên. Mở lại NetBooster với quyền đó?", "Cần quyền quản trị", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) { try { DnsService.RestartAsAdministrator(); Close(); } catch (Exception ex) { ShowError(ex); } }
        return false;
    }
    private async Task RunUiAsync(Func<Task> action) { UseWaitCursor = true; try { await action(); } catch (Exception ex) { _log.Error(ex.Message); ShowError(ex); } finally { UseWaitCursor = false; } }
    private static string Format(PingSummary? x) => x is null ? "không tìm thấy" : $"{x.Received}/{x.Sent} trả lời; mất {x.LossPercent:F0}%; trung vị {x.MedianMs:F0} ms; jitter {x.JitterMs:F0} ms";
    private void Write(string text) => _output.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\r\n");
    private void ShowError(Exception ex) { Write("Lỗi: " + ex.Message); MessageBox.Show(ex.Message, "NetBooster", MessageBoxButtons.OK, MessageBoxIcon.Error); }
}
