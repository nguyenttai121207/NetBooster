using System.Security.Principal;
using NetBooster.Logic;
using NetBooster.Services;

namespace NetBooster.UI;

/// <summary>
/// NetBooster 2.0 Single-Window UI.
/// Adheres to Windows 11 styling (Segoe UI, PerMonitorV2 High DPI, Light/Dark theme).
/// Runs fully asynchronously with interactive progress bar and cancellation.
/// </summary>
public sealed class MainForm : Form
{
    // Services
    private readonly AppLogger _log = new();
    private readonly ProcessRunner _runner = new();
    private readonly NetworkDiagnostics _diagnostics = new();
    private readonly WifiService _wifiService = new();
    private readonly DnsService _dnsService;
    private readonly BufferbloatService _bufferbloatService;
    private readonly NetworkUsageService _usageService;
    private readonly DeliveryOptimizationService _doService;
    private readonly WifiPowerService _wifiPowerService;
    private readonly PowerPlanService _powerPlanService;
    private readonly NetworkRepairService _repairService;
    private readonly SystemRestoreService _restoreService;
    private readonly ScanOrchestrator _orchestrator;

    // Scan state
    private CancellationTokenSource? _scanCts;
    private IReadOnlyList<CardResult>? _lastCards;
    private IReadOnlyList<OptimizationItem>? _lastRecommendations;
    private ScanResult? _lastScanResult;

    // UI Controls
    private readonly Panel _headerPanel;
    private readonly Button _scanButton;
    private readonly ProgressBar _progressBar;
    private readonly Label _progressLabel;
    private readonly Button _cancelButton;
    private readonly FlowLayoutPanel _progressPanel;
    private readonly FlowLayoutPanel _cardsPanel;
    private readonly Panel _recommendationsPanel;
    private readonly CheckedListBox _recommendationsList;
    private readonly Button _applyButton;
    private readonly Button _rollbackButton;
    private readonly CheckBox _createRestorePointCheck;
    private readonly TextBox _detailBox;
    private readonly Panel _advancedPanel;
    private readonly Button _advancedToggle;
    private bool _advancedVisible = false;

    // DNS Controls
    private readonly ComboBox _adapterCombo;
    private readonly Button _dnsBenchmarkButton;
    private readonly Button _dnsApplyButton;
    private readonly Button _dnsRestoreButton;
    private DnsDecision? _lastDnsDecision;

    // Colors
    private readonly Color _bgColor;
    private readonly Color _fgColor;
    private readonly Color _cardBg;
    private readonly Color _accentColor = Color.FromArgb(0, 120, 212); // Windows Accent Blue

    public MainForm()
    {
        // 1. Initialize Services
        _dnsService = new DnsService(_runner, _log);
        _bufferbloatService = new BufferbloatService(_diagnostics, _log);
        _usageService = new NetworkUsageService(_runner, _log);
        _doService = new DeliveryOptimizationService(_runner, _log);
        _wifiPowerService = new WifiPowerService(_runner, _log);
        _powerPlanService = new PowerPlanService(_runner, _log);
        _repairService = new NetworkRepairService(_runner, _log);
        _restoreService = new SystemRestoreService(_runner, _log);
        _orchestrator = new ScanOrchestrator(
            _diagnostics, _wifiService, _dnsService, _bufferbloatService,
            _usageService, _doService, _wifiPowerService, _powerPlanService, _log);

        // 2. Setup Themes (Windows 11 Light/Dark Mode)
        var isDark = IsDarkMode();
        _bgColor = isDark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(248, 249, 250);
        _fgColor = isDark ? Color.FromArgb(245, 245, 245) : Color.FromArgb(33, 37, 41);
        _cardBg = isDark ? Color.FromArgb(45, 45, 45) : Color.White;

        // Form properties
        Text = "NetBooster 2.0 — Tối ưu mạng trung thực";
        Width = 920;
        Height = 780;
        MinimumSize = new Size(820, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = _bgColor;
        ForeColor = _fgColor;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;

        // Main scrollable container
        var mainScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(24, 16, 24, 16)
        };

        var mainFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Width = 850,
            Padding = new Padding(0)
        };
        mainScroll.Resize += (_, _) => mainFlow.Width = Math.Max(780, mainScroll.ClientSize.Width - 48);

        // === HEADER AREA ===
        _headerPanel = new Panel { Height = 105, Width = 850, Margin = new Padding(0, 0, 0, 12) };
        _headerPanel.Resize += (_, _) => _headerPanel.Width = mainFlow.Width;

        var titleLabel = new Label
        {
            Text = "NetBooster 2.0",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = _fgColor,
            AutoSize = true,
            Location = new Point(0, 2)
        };

        var isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        var adminBadge = new Label
        {
            Text = isAdmin ? "🛡️ Đang chạy với quyền Administrator" : "⚠️ Cần quyền Administrator",
            ForeColor = isAdmin ? Color.FromArgb(16, 124, 16) : Color.Crimson,
            Font = new Font("Segoe UI Semibold", 9.5f),
            AutoSize = true,
            Location = new Point(220, 11)
        };

        var subtitleLabel = new Label
        {
            Text = "Chẩn đoán độ ổn định, bufferbloat, nhiễu kênh Wi-Fi và tối ưu mạng máy khách (Client-side).",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(2, 44)
        };

        var disclaimerLabel = new Label
        {
            Text = "Cam kết trung thực: NetBooster KHÔNG tăng băng thông vượt gói cước. Ứng dụng chỉ khắc phục nguyên nhân gây giật/lag.",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
            ForeColor = isDark ? Color.LightGray : Color.DarkSlateGray,
            AutoSize = true,
            Location = new Point(2, 70)
        };

        _headerPanel.Controls.AddRange(new Control[] { titleLabel, adminBadge, subtitleLabel, disclaimerLabel });

        // === MAIN SCAN BUTTON ===
        _scanButton = new Button
        {
            Text = "🔍  Quét Mạng Của Tôi",
            Font = new Font("Segoe UI Semibold", 13),
            BackColor = _accentColor,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Height = 48,
            Width = 850,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 10)
        };
        _scanButton.FlatAppearance.BorderSize = 0;
        _scanButton.Click += async (_, _) => await RunFullScanAsync();

        // === PROGRESS AREA ===
        _progressPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Height = 36,
            Width = 850,
            Visible = false,
            Margin = new Padding(0, 0, 0, 8),
            WrapContents = false
        };
        _progressBar = new ProgressBar
        {
            Width = 620,
            Height = 26,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous
        };
        _progressLabel = new Label
        {
            Text = "0%",
            AutoSize = true,
            Margin = new Padding(10, 5, 10, 0),
            ForeColor = _fgColor,
            Font = new Font("Segoe UI", 9)
        };
        _cancelButton = new Button
        {
            Text = "Hủy",
            Width = 70,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            ForeColor = _fgColor,
            Cursor = Cursors.Hand
        };
        _cancelButton.Click += (_, _) => _scanCts?.Cancel();
        _progressPanel.Controls.AddRange(new Control[] { _progressBar, _progressLabel, _cancelButton });

        // === STATUS CARDS PANEL (2x2 GRID) ===
        _cardsPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = 850,
            Visible = false,
            Margin = new Padding(0, 0, 0, 12)
        };

        // === TECHNICAL DETAIL BOX ===
        _detailBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 130,
            Width = 850,
            Font = new Font("Consolas", 9.5f),
            BackColor = _cardBg,
            ForeColor = _fgColor,
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false,
            Margin = new Padding(0, 0, 0, 12)
        };

        // === RECOMMENDATIONS & ACTIONS PANEL ===
        _recommendationsPanel = new Panel
        {
            Width = 850,
            Height = 270,
            Visible = false,
            Margin = new Padding(0, 0, 0, 14)
        };

        var recTitle = new Label
        {
            Text = "Đề xuất tối ưu hóa (Tích chọn mục muốn áp dụng):",
            Font = new Font("Segoe UI Semibold", 11.5f),
            ForeColor = _fgColor,
            AutoSize = true,
            Location = new Point(0, 0)
        };

        _recommendationsList = new CheckedListBox
        {
            Location = new Point(0, 28),
            Width = 840,
            Height = 145,
            BackColor = _cardBg,
            ForeColor = _fgColor,
            Font = new Font("Segoe UI", 9.5f),
            BorderStyle = BorderStyle.FixedSingle,
            CheckOnClick = true
        };
        _recommendationsList.ItemCheck += (_, _) => BeginInvoke(UpdateApplyButtonState);

        var actionsBar = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Location = new Point(0, 182),
            Width = 840,
            Height = 48,
            WrapContents = false
        };

        _applyButton = new Button
        {
            Text = "✅ Áp dụng mục đã chọn",
            Font = new Font("Segoe UI Semibold", 10),
            BackColor = Color.FromArgb(16, 124, 16),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Height = 40,
            Width = 230,
            Enabled = false,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 12, 0)
        };
        _applyButton.FlatAppearance.BorderSize = 0;
        _applyButton.Click += async (_, _) => await ApplySelectedAsync();

        _rollbackButton = new Button
        {
            Text = "↩️ Hoàn tác tất cả",
            Font = new Font("Segoe UI", 9.5f),
            FlatStyle = FlatStyle.Flat,
            Height = 40,
            Width = 170,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 16, 0),
            ForeColor = _fgColor
        };
        _rollbackButton.Click += async (_, _) => await RollbackAllAsync();

        _createRestorePointCheck = new CheckBox
        {
            Text = "Tạo System Restore Point trước khi áp dụng",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0),
            Font = new Font("Segoe UI", 9),
            ForeColor = _fgColor
        };

        actionsBar.Controls.AddRange(new Control[] { _applyButton, _rollbackButton, _createRestorePointCheck });
        _recommendationsPanel.Controls.AddRange(new Control[] { recTitle, _recommendationsList, actionsBar });

        // === ADVANCED SECTION (COLLAPSIBLE) ===
        _advancedToggle = new Button
        {
            Text = "⚙️ Công cụ Nâng cao ▼",
            Font = new Font("Segoe UI Semibold", 10),
            FlatStyle = FlatStyle.Flat,
            Height = 36,
            Width = 220,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 8),
            ForeColor = _fgColor
        };
        _advancedToggle.Click += (_, _) => ToggleAdvanced();

        _advancedPanel = new Panel
        {
            Width = 850,
            Height = 480,
            Visible = false,
            Margin = new Padding(0, 0, 0, 16)
        };

        // Advanced Panel Controls
        _adapterCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 260,
            BackColor = _cardBg,
            ForeColor = _fgColor
        };
        _dnsBenchmarkButton = new Button { Text = "Đo benchmark DNS", FlatStyle = FlatStyle.Flat, Width = 140, Height = 28, ForeColor = _fgColor };
        _dnsBenchmarkButton.Click += async (_, _) => await BenchmarkDnsAsync();
        _dnsApplyButton = new Button { Text = "Áp dụng DNS tốt nhất", FlatStyle = FlatStyle.Flat, Width = 170, Height = 28, Enabled = false, ForeColor = _fgColor };
        _dnsApplyButton.Click += async (_, _) => await ApplyBestDnsAsync();
        _dnsRestoreButton = new Button { Text = "Khôi phục DNS gốc", FlatStyle = FlatStyle.Flat, Width = 140, Height = 28, ForeColor = _fgColor };
        _dnsRestoreButton.Click += async (_, _) => await RestoreDnsAsync();

        BuildAdvancedPanel();

        // Assemble All into Flow Layout
        mainFlow.Controls.Add(_headerPanel);
        mainFlow.Controls.Add(_scanButton);
        mainFlow.Controls.Add(_progressPanel);
        mainFlow.Controls.Add(_cardsPanel);
        mainFlow.Controls.Add(_detailBox);
        mainFlow.Controls.Add(_recommendationsPanel);
        mainFlow.Controls.Add(_advancedToggle);
        mainFlow.Controls.Add(_advancedPanel);

        mainScroll.Controls.Add(mainFlow);
        Controls.Add(mainScroll);
    }

    private void BuildAdvancedPanel()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0)
        };

        // Section: DNS Benchmark
        flow.Controls.Add(SectionTitle("1. DNS Benchmark chuyên sâu (IPv4 + IPv6, Cold/Hot Cache)"));
        var dnsRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 36, Width = 830, WrapContents = false };
        dnsRow.Controls.Add(new Label { Text = "Card mạng:", AutoSize = true, Margin = new Padding(0, 6, 6, 0), ForeColor = _fgColor });
        dnsRow.Controls.Add(_adapterCombo);
        dnsRow.Controls.Add(_dnsBenchmarkButton);
        dnsRow.Controls.Add(_dnsApplyButton);
        dnsRow.Controls.Add(_dnsRestoreButton);
        flow.Controls.Add(dnsRow);

        // Section: Delivery Optimization
        flow.Controls.Add(SectionTitle("2. Windows Update Delivery Optimization (Giới hạn P2P)"));
        var doRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 36, Width = 830, WrapContents = false };
        var doStatusLabel = new Label { Text = "Chưa kiểm tra", AutoSize = true, Margin = new Padding(0, 6, 12, 0), ForeColor = _fgColor };
        var doCheckBtn = new Button { Text = "Kiểm tra", FlatStyle = FlatStyle.Flat, Width = 85, Height = 28, ForeColor = _fgColor };
        doCheckBtn.Click += async (_, _) =>
        {
            try
            {
                var st = await _doService.GetStateAsync();
                doStatusLabel.Text = st.Message;
            }
            catch (Exception ex) { ShowError(ex); }
        };
        var doLimitBtn = new Button { Text = "Giới hạn nền 10%", FlatStyle = FlatStyle.Flat, Width = 130, Height = 28, ForeColor = _fgColor };
        doLimitBtn.Click += async (_, _) =>
        {
            if (!ConfirmAction("Giới hạn băng thông nền của Windows Update ở 10%?")) return;
            try { await _doService.LimitBackgroundTo10PercentAsync(); WriteDetail("✅ Đã đặt giới hạn Delivery Optimization ở 10%."); }
            catch (Exception ex) { ShowError(ex); }
        };
        var doRestoreBtn = new Button { Text = "Khôi phục", FlatStyle = FlatStyle.Flat, Width = 90, Height = 28, ForeColor = _fgColor };
        doRestoreBtn.Click += async (_, _) =>
        {
            try { await _doService.RestoreAsync(); WriteDetail("✅ Đã khôi phục Delivery Optimization."); }
            catch (Exception ex) { ShowError(ex); }
        };
        doRow.Controls.AddRange(new Control[] { doStatusLabel, doCheckBtn, doLimitBtn, doRestoreBtn });
        flow.Controls.Add(doRow);

        // Section: Network Repair
        flow.Controls.Add(SectionTitle("3. Sửa lỗi mạng (Network Repair)"));
        foreach (var action in NetworkRepairService.GetAvailableActions())
        {
            var pnl = new Panel { Width = 830, Height = 64, Margin = new Padding(0, 2, 0, 4), BackColor = _cardBg };
            var lblTitle = new Label
            {
                Text = action.Name + (action.RequiresReboot ? " ⚠️ (Cần khởi động lại)" : ""),
                Font = new Font("Segoe UI Semibold", 9.5f),
                AutoSize = true,
                Location = new Point(8, 4),
                ForeColor = _fgColor
            };
            var lblDesc = new Label
            {
                Text = $"{action.Description} | Khi nào dùng: {action.WhenToUse}",
                AutoSize = true,
                MaximumSize = new Size(670, 0),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(8, 24),
                ForeColor = Color.Gray
            };
            var btnExec = new Button
            {
                Text = "Thực hiện",
                FlatStyle = FlatStyle.Flat,
                Width = 90,
                Height = 28,
                Location = new Point(730, 16),
                ForeColor = _fgColor,
                Cursor = Cursors.Hand
            };
            var act = action;
            btnExec.Click += async (_, _) =>
            {
                var warn = act.RequiresReboot ? "\n\n⚠️ Lưu ý: Thao tác này sẽ yêu cầu khởi động lại máy!" : "";
                if (!ConfirmAction($"Bạn có chắc chắn muốn thực hiện: {act.Name}?{warn}")) return;
                try
                {
                    var res = act.Id switch
                    {
                        "renew-ip" => await _repairService.RenewIpAsync(),
                        "flush-dns" => await _repairService.FlushDnsAsync(),
                        "flush-arp" => await _repairService.FlushArpAsync(),
                        "reset-winsock" => await _repairService.ResetWinsockAsync(),
                        "reset-tcpip" => await _repairService.ResetTcpIpAsync(),
                        _ => "Hành động không xác định."
                    };
                    WriteDetail($"✅ {res}");
                }
                catch (Exception ex) { ShowError(ex); }
            };
            pnl.Controls.AddRange(new Control[] { lblTitle, lblDesc, btnExec });
            flow.Controls.Add(pnl);
        }

        // Section: Activity Log
        flow.Controls.Add(SectionTitle("4. Nhật ký hoạt động (operations.log)"));
        var logBtn = new Button { Text = "Tải lại và hiển thị nhật ký", FlatStyle = FlatStyle.Flat, Width = 200, Height = 28, ForeColor = _fgColor };
        logBtn.Click += (_, _) =>
        {
            _detailBox.Visible = true;
            _detailBox.Text = _log.ReadAll();
            _detailBox.SelectionStart = _detailBox.Text.Length;
            _detailBox.ScrollToCaret();
        };
        flow.Controls.Add(logBtn);

        _advancedPanel.Controls.Add(flow);
    }

    // ========== SCAN WORKFLOW ==========

    private async Task RunFullScanAsync()
    {
        _scanButton.Enabled = false;
        _progressPanel.Visible = true;
        _progressBar.Value = 0;
        _progressLabel.Text = "0% — Đang khởi động chẩn đoán...";
        _cardsPanel.Visible = false;
        _recommendationsPanel.Visible = false;
        _detailBox.Visible = false;

        _scanCts = new CancellationTokenSource();
        var progress = new Progress<ScanOrchestrator.ScanProgress>(p =>
        {
            _progressBar.Value = Math.Clamp(p.Percent, 0, 100);
            _progressLabel.Text = $"{p.Percent}% — {p.Step}";
        });

        try
        {
            var loadUrl = BufferbloatService.DefaultLoadUrl;
            var (cards, recommendations, scanResult) = await _orchestrator.RunFullScanAsync(loadUrl, progress, _scanCts.Token);
            _lastCards = cards;
            _lastRecommendations = recommendations;
            _lastScanResult = scanResult;

            // Render 4 status cards
            _cardsPanel.Controls.Clear();
            foreach (var card in cards)
            {
                var cardPanel = CreateCardPanel(card);
                _cardsPanel.Controls.Add(cardPanel);
            }
            _cardsPanel.Visible = true;

            // Render recommendations list
            _recommendationsList.Items.Clear();
            foreach (var rec in recommendations)
            {
                var idx = _recommendationsList.Items.Add($"[{rec.Category}] {rec.Title}: {rec.Description}");
                _recommendationsList.SetItemChecked(idx, rec.IsSelected);
            }
            _recommendationsPanel.Visible = recommendations.Count > 0;
            UpdateApplyButtonState();

            // Refresh adapter combo in Advanced panel
            LoadAdaptersIntoCombo();
        }
        catch (OperationCanceledException)
        {
            WriteDetail("ℹ️ Người dùng đã hủy quá trình quét.");
        }
        catch (Exception ex)
        {
            _log.Error("Lỗi quá trình quét: " + ex.Message);
            ShowError(ex);
        }
        finally
        {
            _scanButton.Enabled = true;
            _progressPanel.Visible = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private Panel CreateCardPanel(CardResult card)
    {
        var borderColor = card.Status switch
        {
            StatusLevel.Good => Color.FromArgb(16, 124, 16),
            StatusLevel.Warning => Color.FromArgb(255, 185, 0),
            StatusLevel.Bad => Color.FromArgb(196, 43, 28),
            _ => Color.Gray
        };
        var statusIcon = card.Status switch
        {
            StatusLevel.Good => "✅",
            StatusLevel.Warning => "⚠️",
            StatusLevel.Bad => "🔴",
            _ => "ℹ️"
        };

        var panel = new Panel
        {
            Width = 410,
            Height = 88,
            Margin = new Padding(6),
            BackColor = _cardBg,
            Cursor = Cursors.Hand
        };

        var leftBorder = new Panel { Width = 5, Height = 88, BackColor = borderColor, Dock = DockStyle.Left };

        var titleLabel = new Label
        {
            Text = $"{statusIcon} {card.Title}",
            Font = new Font("Segoe UI Semibold", 10.5f),
            AutoSize = true,
            Location = new Point(14, 8),
            ForeColor = _fgColor
        };

        var summaryLabel = new Label
        {
            Text = card.ShortSummary,
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Location = new Point(14, 32),
            ForeColor = Color.Gray
        };

        var detailLink = new LinkLabel
        {
            Text = "Xem chi tiết...",
            AutoSize = true,
            Location = new Point(14, 62),
            Font = new Font("Segoe UI", 8.5f)
        };

        var captured = card;
        Action showDetail = () =>
        {
            _detailBox.Visible = true;
            _detailBox.Text = $"=== {captured.Title} ===\r\n\r\n{captured.Details}";
        };

        detailLink.LinkClicked += (_, _) => showDetail();
        panel.Click += (_, _) => showDetail();

        panel.Controls.AddRange(new Control[] { leftBorder, titleLabel, summaryLabel, detailLink });
        return panel;
    }

    // ========== APPLY / ROLLBACK WORKFLOW ==========

    private async Task ApplySelectedAsync()
    {
        if (_lastRecommendations == null) return;

        var selected = new List<OptimizationItem>();
        for (int i = 0; i < _recommendationsList.Items.Count; i++)
        {
            if (_recommendationsList.GetItemChecked(i) && i < _lastRecommendations.Count)
                selected.Add(_lastRecommendations[i]);
        }

        if (selected.Count == 0) return;

        // Requirement a & b: Detailed summary before applying & active user confirmation
        var summaryText = "BẢNG TÓM TẮT CÁC THAY ĐỔI SẮP ÁP DỤNG:\r\n\r\n" +
                          string.Join("\r\n", selected.Select((s, idx) => $"{idx + 1}. [{s.Category}] {s.Title}\r\n   -> {s.Description}")) +
                          "\r\n\r\nCấu hình gốc sẽ được tự động sao lưu tại:\r\n%ProgramData%\\NetBooster\\backups\\\r\n\r\nBạn có chắc chắn muốn tiến hành tối ưu?";

        if (!ConfirmAction(summaryText)) return;

        _applyButton.Enabled = false;
        _rollbackButton.Enabled = false;

        // Requirement: System Restore Point - only created when user applies and changes involve network card / power
        bool touchesPowerOrNic = selected.Any(s => s.Id == "wifi-power" || s.Id == "power-plan" || s.Id.StartsWith("driver-"));
        if (_createRestorePointCheck.Checked && touchesPowerOrNic)
        {
            WriteDetail("Đang tạo System Restore Point qua PowerShell 5.1...");
            var rpResult = await _restoreService.TryCreateRestorePointAsync();
            WriteDetail(rpResult.Message);
        }

        var errors = new List<string>();

        foreach (var item in selected)
        {
            try
            {
                switch (item.Id)
                {
                    case "wifi-power":
                        var adapters = _wifiService.GetAdapters();
                        var wifiAdapter = adapters.FirstOrDefault(a => a.IsWifi && a.IsUp);
                        if (wifiAdapter != null)
                        {
                            await _wifiPowerService.DisableSupportedSavingAsync(wifiAdapter);
                            WriteDetail("✅ Đã tắt tiết kiệm điện cho card Wi-Fi.");
                        }
                        break;

                    case "driver-band":
                    case "driver-roaming":
                        var wAdapters = _wifiService.GetAdapters();
                        var wAdapter = wAdapters.FirstOrDefault(a => a.IsWifi && a.IsUp);
                        if (wAdapter != null)
                        {
                            await _wifiPowerService.OptimizeAdvancedPropertiesAsync(wAdapter);
                            WriteDetail("✅ Đã tối ưu thuộc tính nâng cao của driver Wi-Fi.");
                        }
                        break;

                    case "power-plan":
                        var ppMsg = await _powerPlanService.SetHighPerformanceAsync();
                        WriteDetail($"✅ {ppMsg}");
                        break;

                    case "do-limit":
                        await _doService.LimitBackgroundTo10PercentAsync();
                        WriteDetail("✅ Đã đặt giới hạn Delivery Optimization nền ở mức 10%.");
                        break;

                    default:
                        if (item.Id.StartsWith("router-channel"))
                        {
                            WriteDetail($"ℹ️ Gợi ý Router: {item.Description}");
                        }
                        break;
                }
            }
            catch (NotSupportedException nex)
            {
                WriteDetail($"⚠️ {item.Title}: Không hỗ trợ trên máy này ({nex.Message}).");
            }
            catch (Exception ex)
            {
                errors.Add($"[{item.Title}]: {ex.Message}");
                _log.Error($"Lỗi áp dụng {item.Id}: {ex.Message}");
            }
        }

        _rollbackButton.Enabled = true;
        UpdateApplyButtonState();

        if (errors.Count > 0)
        {
            MessageBox.Show("Một số mục không thể áp dụng hoặc đã tự động rollback:\r\n\r\n" + string.Join("\r\n", errors),
                "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            WriteDetail("🎉 Đã hoàn tất áp dụng các mục tối ưu đã chọn!");
        }
    }

    private async Task RollbackAllAsync()
    {
        if (!ConfirmAction("Bạn có chắc chắn muốn Hoàn tác (Rollback) toàn bộ cấu hình về trạng thái ban đầu?\r\n\r\nSẽ khôi phục: Power Plan, Wi-Fi Power Management, Thuộc tính Driver, DNS, Delivery Optimization."))
            return;

        _rollbackButton.Enabled = false;
        var errors = new List<string>();

        // Rollback Wi-Fi Power
        try { await _wifiPowerService.RestorePowerAsync(); WriteDetail("✅ Đã khôi phục cài đặt nguồn Card Wi-Fi."); }
        catch (Exception ex) { errors.Add("Wi-Fi Power: " + ex.Message); }

        // Rollback Driver Properties
        try { await _wifiPowerService.RestoreAdvancedPropertiesAsync(); WriteDetail("✅ Đã khôi phục thuộc tính driver Wi-Fi."); }
        catch (Exception ex) { errors.Add("Wi-Fi Driver: " + ex.Message); }

        // Rollback Power Plan
        try { await _powerPlanService.RestoreAsync(); WriteDetail("✅ Đã khôi phục Power Plan gốc."); }
        catch (Exception ex) { errors.Add("Power Plan: " + ex.Message); }

        // Rollback DNS
        try { await _dnsService.RestoreSavedAsync(); WriteDetail("✅ Đã khôi phục cấu hình DNS."); }
        catch (Exception ex) { errors.Add("DNS: " + ex.Message); }

        // Rollback Delivery Optimization
        try { await _doService.RestoreAsync(); WriteDetail("✅ Đã khôi phục cấu hình Delivery Optimization."); }
        catch (Exception ex) { errors.Add("Delivery Optimization: " + ex.Message); }

        _rollbackButton.Enabled = true;

        if (errors.Count > 0)
        {
            MessageBox.Show("Kết quả hoàn tác:\r\n\r\n" + string.Join("\r\n", errors), "Hoàn tác", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            WriteDetail("🎉 Đã hoàn tác tất cả các thay đổi thành công!");
        }
    }

    // ========== DNS BENCHMARK (ADVANCED) ==========

    private async Task BenchmarkDnsAsync()
    {
        if (_adapterCombo.SelectedItem == null)
        {
            LoadAdaptersIntoCombo();
            if (_adapterCombo.SelectedItem == null)
            {
                MessageBox.Show("Không tìm thấy card mạng hợp lệ.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        var adapter = (NetworkAdapterInfo)_adapterCombo.SelectedItem;
        _dnsBenchmarkButton.Enabled = false;
        _detailBox.Visible = true;
        _detailBox.Text = $"Đang tiến hành đo benchmark DNS trên '{adapter.Name}'...\r\nĐo 7 tên miền phổ biến × Cache Lạnh/Nóng (Cold/Hot) × IPv4/IPv6...\r\n";

        try
        {
            var currentProv = await _dnsService.GetCurrentProviderAsync(adapter.InterfaceIndex);
            var providers = new List<DnsProvider>();
            if (currentProv != null) providers.Add(currentProv);
            providers.AddRange(DnsService.PublicProviders);

            var benchmarks = new List<DnsProviderBenchmark>();
            foreach (var prov in providers)
            {
                _detailBox.AppendText($"Đang đo: {prov.DisplayName}...\r\n");
                var b = await _dnsService.MeasureProviderAsync(prov);
                benchmarks.Add(b);
                _detailBox.AppendText($"  -> Trung vị: {b.MedianMs:F0} ms | Tỷ lệ lỗi: {b.FailurePercent:F0}%\r\n");
            }

            var currBm = currentProv != null ? benchmarks.FirstOrDefault(x => x.Provider.Id == "current") : null;
            var candidates = benchmarks.Where(x => x.Provider.Id != "current");
            var decision = DnsBenchmarkLogic.Decide(currBm, candidates);
            _lastDnsDecision = decision;

            _detailBox.AppendText($"\r\nKẾT LUẬN: {decision.Reason}\r\n");

            if (decision.ShouldApply && decision.Winner != null)
            {
                _dnsApplyButton.Enabled = true;
                _dnsApplyButton.Text = $"Áp dụng {decision.Winner.DisplayName}";
            }
            else
            {
                _dnsApplyButton.Enabled = false;
            }

            _log.Info("Đã hoàn tất DNS benchmark.");
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _dnsBenchmarkButton.Enabled = true; }
    }

    private async Task ApplyBestDnsAsync()
    {
        if (_lastDnsDecision?.Winner == null || _adapterCombo.SelectedItem == null) return;
        var adapter = (NetworkAdapterInfo)_adapterCombo.SelectedItem;
        var winner = _lastDnsDecision.Winner;

        var msg = $"Đổi DNS của '{adapter.Name}' sang {winner.DisplayName}?\r\n\r\n" +
                  $"• IPv4: {string.Join(", ", winner.IPv4)}\r\n" +
                  $"• IPv6: {string.Join(", ", winner.IPv6)}\r\n\r\n" +
                  "Cấu hình DNS cũ sẽ được sao lưu và tự khôi phục nếu lệnh gặp sự cố.";

        if (!ConfirmAction(msg)) return;

        try
        {
            await _dnsService.ApplyAsync(adapter.InterfaceIndex, winner);
            WriteDetail($"✅ Đã đổi DNS sang {winner.DisplayName} thành công.");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task RestoreDnsAsync()
    {
        if (!ConfirmAction("Khôi phục cấu hình DNS từ bản sao lưu gần nhất?")) return;
        try
        {
            await _dnsService.RestoreSavedAsync();
            WriteDetail("✅ Đã khôi phục cấu hình DNS ban đầu.");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ========== HELPERS ==========

    private void ToggleAdvanced()
    {
        _advancedVisible = !_advancedVisible;
        _advancedPanel.Visible = _advancedVisible;
        _advancedToggle.Text = _advancedVisible ? "⚙️ Thu gọn Nâng cao ▲" : "⚙️ Công cụ Nâng cao ▼";
        if (_advancedVisible) LoadAdaptersIntoCombo();
    }

    private void LoadAdaptersIntoCombo()
    {
        var adapters = _wifiService.GetAdapters().Where(a => a.IsUp).ToArray();
        _adapterCombo.Items.Clear();
        foreach (var a in adapters) _adapterCombo.Items.Add(a);
        if (_adapterCombo.Items.Count > 0 && _adapterCombo.SelectedIndex < 0)
            _adapterCombo.SelectedIndex = 0;
    }

    private void UpdateApplyButtonState()
    {
        _applyButton.Enabled = _recommendationsList.CheckedItems.Count > 0;
    }

    private void WriteDetail(string text)
    {
        _detailBox.Visible = true;
        _detailBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\r\n");
    }

    private void ShowError(Exception ex)
    {
        WriteDetail("❌ Lỗi: " + ex.Message);
        MessageBox.Show(ex.Message, "NetBooster", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static bool ConfirmAction(string message) =>
        MessageBox.Show(message, "Xác nhận tác vụ", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private Label SectionTitle(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI Semibold", 10.5f),
        ForeColor = _fgColor,
        AutoSize = true,
        Margin = new Padding(0, 14, 0, 4)
    };

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            return val is int i && i == 0;
        }
        catch { return false; }
    }
}
