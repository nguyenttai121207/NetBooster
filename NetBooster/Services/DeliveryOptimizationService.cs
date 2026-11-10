using System.Text.Json;

namespace NetBooster.Services;

public sealed class DeliveryOptimizationService
{
    private readonly ProcessRunner _runner;
    private readonly AppLogger _log;
    private readonly string _backupPath = Path.Combine(AppPaths.CommonDataDirectory, "delivery-optimization-backup.json");
    public DeliveryOptimizationService(ProcessRunner runner, AppLogger log) { _runner = runner; _log = log; }

    public async Task<DeliveryOptimizationState> GetStateAsync()
    {
        var script = "if (-not (Get-Command Get-DOPercentageMaxBackgroundBandwidth -ErrorAction SilentlyContinue)) { 'UNSUPPORTED' } else { Get-DOPercentageMaxBackgroundBandwidth }";
        var r = await _runner.PowerShellAsync(script);
        if (r.ExitCode != 0 || r.StdOut.Trim().Equals("UNSUPPORTED", StringComparison.OrdinalIgnoreCase)) return new(false, null, "Windows không có cmdlet Delivery Optimization cần thiết.");
        return int.TryParse(r.StdOut.Trim(), out var percent) && percent is >= 0 and <= 100
            ? new(true, percent, "Đã đọc giới hạn băng thông nền Delivery Optimization.")
            : new(true, null, "Không có giới hạn phần trăm nền đang áp dụng hoặc bị chính sách quản lý.");
    }

    public async Task LimitBackgroundTo10PercentAsync()
    {
        var state = await GetStateAsync();
        if (!state.IsSupported) throw new InvalidOperationException(state.Message);
        Directory.CreateDirectory(AppPaths.CommonDataDirectory);
        await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(new DeliveryOptimizationBackup(state.BackgroundPercent, DateTime.UtcNow)));
        var r = await _runner.PowerShellAsync("Set-DOPercentageMaxBackgroundBandwidth -MaxBackgroundBandwidth 10 -ErrorAction Stop");
        if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
        _log.Info("Đã đặt giới hạn Delivery Optimization nền 10% bằng cmdlet Windows.");
    }

    public async Task RestoreAsync()
    {
        if (!File.Exists(_backupPath)) throw new InvalidOperationException("Chưa có bản sao lưu Delivery Optimization trong %ProgramData%\\NetBooster.");
        var backup = JsonSerializer.Deserialize<DeliveryOptimizationBackup>(await File.ReadAllTextAsync(_backupPath)) ?? throw new InvalidOperationException("Bản sao lưu Delivery Optimization không hợp lệ.");
        var script = backup.BackgroundPercent is >= 0 and <= 100
            ? $"Set-DOPercentageMaxBackgroundBandwidth -MaxBackgroundBandwidth {backup.BackgroundPercent.Value} -ErrorAction Stop"
            : "Set-DOPercentageMaxBackgroundBandwidth -Reset -ErrorAction Stop";
        var r = await _runner.PowerShellAsync(script);
        if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
        _log.Info("Đã khôi phục giới hạn Delivery Optimization nền.");
    }

    private static string ErrorText((int ExitCode, string StdOut, string StdErr) r) => string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut.Trim() : r.StdErr.Trim();
}
