using System.Text.Json;
using System.Text.RegularExpressions;
using NetBooster.Logic;

namespace NetBooster.Services;

/// <summary>
/// Manages Windows Power Plans with fallback mechanisms:
/// 1. Tries activating High Performance directly.
/// 2. If missing, attempts to duplicate SCHEME_MIN (8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c).
/// 3. If that fails, attempts Windows 11 power mode overlay.
/// 4. If all fail, gracefully reports "Máy không hỗ trợ" and skips.
/// Checks battery status to prevent enabling by default on battery power.
/// </summary>
public sealed class PowerPlanService
{
    private static readonly Guid HighPerformanceGuid = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Regex GuidPattern = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
    private readonly ProcessRunner _runner;
    private readonly AppLogger _log;
    private readonly string _backupPath = Path.Combine(AppPaths.CommonDataDirectory, "power-plan-backup.json");

    public PowerPlanService(ProcessRunner runner, AppLogger log)
    {
        _runner = runner;
        _log = log;
    }

    public static bool IsRunningOnBattery()
    {
        try
        {
            return SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
        }
        catch { return false; }
    }

    public async Task<Guid?> GetActiveSchemeAsync()
    {
        var r = await _runner.RunAsync("powercfg.exe", "/getactivescheme");
        return r.ExitCode == 0 && PowerPlanLogic.TryParseActiveScheme(r.StdOut, out var scheme) ? scheme : null;
    }

    public async Task<bool> IsHighPerformanceActiveAsync()
    {
        var active = await GetActiveSchemeAsync();
        if (active == null) return false;
        if (active.Value == HighPerformanceGuid) return true;
        
        // Also check if current plan name contains "High performance" or "Tối đa"
        var r = await _runner.RunAsync("powercfg.exe", "/getactivescheme");
        return r.StdOut.Contains("High performance", StringComparison.OrdinalIgnoreCase) ||
               r.StdOut.Contains("Ultimate", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> SetHighPerformanceAsync()
    {
        var current = await GetActiveSchemeAsync();
        if (current == null) throw new InvalidOperationException("Không đọc được power plan đang hoạt động.");

        // Backup current scheme
        Directory.CreateDirectory(AppPaths.CommonDataDirectory);
        await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(new PowerPlanBackup(current.Value, null, DateTime.UtcNow)));

        // Attempt 1: Check existing schemes in powercfg /list
        var listRes = await _runner.RunAsync("powercfg.exe", "/list");
        if (listRes.ExitCode == 0)
        {
            var match = Regex.Match(listRes.StdOut, @"Power Scheme GUID:\s*([0-9a-fA-F-]{36})\s*\((.*?High\s*performance.*?|.*?Hiệu suất cao.*?)\)", RegexOptions.IgnoreCase);
            if (match.Success && Guid.TryParse(match.Groups[1].Value, out var existingHpGuid))
            {
                var actRes = await _runner.RunAsync("powercfg.exe", $"/setactive {existingHpGuid}");
                if (actRes.ExitCode == 0)
                {
                    _log.Info($"Đã kích hoạt High Performance scheme sẵn có: {existingHpGuid}");
                    return "Đã kích hoạt chế độ High Performance.";
                }
            }
        }

        // Attempt 2: Try powercfg /setactive SCHEME_MIN
        var directRes = await _runner.RunAsync("powercfg.exe", "/setactive SCHEME_MIN");
        if (directRes.ExitCode == 0)
        {
            _log.Info("Đã kích hoạt SCHEME_MIN trực tiếp.");
            return "Đã chuyển Power Plan sang High Performance.";
        }

        // Attempt 3: Try powercfg -duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
        var dupRes = await _runner.RunAsync("powercfg.exe", $"-duplicatescheme {HighPerformanceGuid}");
        if (dupRes.ExitCode == 0)
        {
            var match = GuidPattern.Match(dupRes.StdOut);
            if (match.Success && Guid.TryParse(match.Value, out var newGuid))
            {
                var actRes = await _runner.RunAsync("powercfg.exe", $"/setactive {newGuid}");
                if (actRes.ExitCode == 0)
                {
                    _log.Info($"Đã sao chép và kích hoạt High Performance scheme: {newGuid}");
                    return "Đã sao chép và chuyển sang chế độ High Performance.";
                }
            }
        }

        // Attempt 4: Windows 11 Power Mode overlay (set active overlay to Best Performance)
        // In Windows 11 modern standby systems, overlay GUID 961cc777-2547-4f93-8174-7d86181b8a7a corresponds to High Performance
        var overlayScript = @"
try {
    # Set Windows 11 power mode slider overlay to Best Performance (index 0)
    powercfg /setacvalueindex SCHEME_CURRENT SUB_NONE 0012ee47-9041-4b5d-9b77-535fba8b1442 0
    powercfg /setactive SCHEME_CURRENT
    'OK'
} catch { 'FAIL' }";
        var overlayRes = await _runner.PowerShellAsync(overlayScript);
        if (overlayRes.ExitCode == 0 && overlayRes.StdOut.Contains("OK"))
        {
            _log.Info("Đã áp dụng Windows 11 Best Performance overlay.");
            return "Đã bật chế độ Hiệu suất tốt nhất (Windows 11 Power Overlay).";
        }

        _log.Info("Máy không hỗ trợ High Performance hoặc bị khóa bởi nhà sản xuất (Modern Standby S0).");
        throw new NotSupportedException("Máy không hỗ trợ High Performance plan hoặc bị khóa bởi nhà sản xuất phần cứng.");
    }

    public async Task RestoreAsync()
    {
        if (!File.Exists(_backupPath)) throw new InvalidOperationException("Chưa có bản sao lưu power plan trong %ProgramData%\\NetBooster.");
        var backup = JsonSerializer.Deserialize<PowerPlanBackup>(await File.ReadAllTextAsync(_backupPath)) ?? throw new InvalidOperationException("Bản sao lưu power plan không hợp lệ.");
        var r = await _runner.RunAsync("powercfg.exe", "/setactive " + backup.SchemeGuid.ToString("D"));
        if (r.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut.Trim() : r.StdErr.Trim());
        _log.Info($"Đã khôi phục power plan {backup.SchemeGuid}.");
    }
}
