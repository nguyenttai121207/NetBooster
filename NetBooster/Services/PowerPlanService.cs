using NetBooster.Logic;
using System.Text.Json;

namespace NetBooster.Services;

public sealed class PowerPlanService
{
    private readonly ProcessRunner _runner;
    private readonly AppLogger _log;
    private readonly string _backupPath = Path.Combine(AppPaths.CommonDataDirectory, "power-plan-backup.json");
    public PowerPlanService(ProcessRunner runner, AppLogger log) { _runner = runner; _log = log; }

    public async Task<Guid?> GetActiveSchemeAsync()
    {
        var r = await _runner.RunAsync("powercfg.exe", "/getactivescheme");
        return r.ExitCode == 0 && PowerPlanLogic.TryParseActiveScheme(r.StdOut, out var scheme) ? scheme : null;
    }

    public async Task SetHighPerformanceAsync()
    {
        var current = await GetActiveSchemeAsync() ?? throw new InvalidOperationException("Không đọc được power plan đang hoạt động.");
        Directory.CreateDirectory(AppPaths.CommonDataDirectory);
        await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(new PowerPlanBackup(current, DateTime.UtcNow)));
        var r = await _runner.RunAsync("powercfg.exe", "/setactive SCHEME_MIN");
        if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
        _log.Info("Đã chuyển power plan sang High performance (SCHEME_MIN).");
    }

    public async Task RestoreAsync()
    {
        if (!File.Exists(_backupPath)) throw new InvalidOperationException("Chưa có bản sao lưu power plan trong %ProgramData%\\NetBooster.");
        var backup = JsonSerializer.Deserialize<PowerPlanBackup>(await File.ReadAllTextAsync(_backupPath)) ?? throw new InvalidOperationException("Bản sao lưu power plan không hợp lệ.");
        var r = await _runner.RunAsync("powercfg.exe", "/setactive " + backup.SchemeGuid.ToString("D"));
        if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
        _log.Info("Đã khôi phục power plan trước đó.");
    }

    private static string ErrorText((int ExitCode, string StdOut, string StdErr) r) => string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut.Trim() : r.StdErr.Trim();
}
