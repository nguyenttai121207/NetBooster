namespace NetBooster.Services;

/// <summary>
/// Service to create System Restore Points using Windows PowerShell 5.1 Checkpoint-Computer.
/// Strictly catches both exceptions and warning streams (24-hour frequency limit, System Protection disabled).
/// Never touches SystemRestorePointCreationFrequency registry.
/// Emits gentle warnings without crashing or stopping the optimization flow.
/// </summary>
public sealed class SystemRestoreService
{
    private readonly ProcessRunner _runner;
    private readonly AppLogger _log;

    public SystemRestoreService(ProcessRunner runner, AppLogger log)
    {
        _runner = runner;
        _log = log;
    }

    public sealed record RestorePointResult(bool Success, bool IsWarningOnly, string Message);

    public async Task<RestorePointResult> TryCreateRestorePointAsync()
    {
        try
        {
            var script = @"
$ErrorActionPreference = 'Stop'
$WarningPreference = 'Continue'
$warns = @()
try {
    Checkpoint-Computer -Description 'NetBooster 2.0 - Trước khi tối ưu' -RestorePointType 'MODIFY_SETTINGS' -WarningVariable wv -ErrorAction Stop
    if ($wv -and $wv.Count -gt 0) {
        $warnText = ($wv | ForEach-Object { $_.Message }) -join '; '
        Write-Output ""WARN:$warnText""
    } else {
        Write-Output 'OK'
    }
} catch {
    $msg = $_.Exception.Message
    if ($msg -match 'frequency|24|cannot be created') {
        Write-Output 'FREQ_LIMIT'
    } elseif ($msg -match 'protection.*disabled|not enabled|turned off') {
        Write-Output 'DISABLED'
    } else {
        Write-Output ""ERROR:$msg""
    }
}";
            // Explicitly call powershell.exe (Windows PowerShell 5.1)
            var r = await _runner.PowerShellAsync(script, 120000);
            var output = r.StdOut.Trim();

            if (output.StartsWith("OK"))
            {
                _log.Info("Đã tạo System Restore Point thành công.");
                return new RestorePointResult(true, false, "Đã tạo điểm khôi phục hệ thống (System Restore Point) thành công.");
            }

            if (output.StartsWith("FREQ_LIMIT") || output.Contains("frequency") || output.Contains("24"))
            {
                _log.Info("Không tạo được Restore Point do giới hạn tần suất 24h của Windows.");
                return new RestorePointResult(false, true, "Bỏ qua tạo điểm khôi phục: Windows giới hạn 1 điểm khôi phục mỗi 24 giờ. Điểm gần nhất vẫn đang hoạt động.");
            }

            if (output.StartsWith("DISABLED") || output.Contains("disabled") || output.Contains("turned off"))
            {
                _log.Info("Không tạo được Restore Point do System Protection đang tắt.");
                return new RestorePointResult(false, true, "Bỏ qua tạo điểm khôi phục: Tính năng System Protection đang tắt trên ổ C:. Tiếp tục áp dụng tối ưu.");
            }

            if (output.StartsWith("WARN:"))
            {
                var warnText = output[5..];
                _log.Info($"Cảnh báo Restore Point: {warnText}");
                return new RestorePointResult(false, true, $"Cảnh báo điểm khôi phục: {warnText}. Tiếp tục áp dụng tối ưu.");
            }

            var errText = output.StartsWith("ERROR:") ? output[6..] : (string.IsNullOrWhiteSpace(r.StdErr) ? output : r.StdErr.Trim());
            _log.Info($"Không thể tạo Restore Point: {errText}");
            return new RestorePointResult(false, true, $"Không thể tạo điểm khôi phục ({errText}). Tiếp tục áp dụng tối ưu.");
        }
        catch (TimeoutException)
        {
            _log.Info("Tạo Restore Point quá thời gian (2 phút), bỏ qua.");
            return new RestorePointResult(false, true, "Tạo điểm khôi phục quá thời gian chờ (2 phút). Tiếp tục áp dụng tối ưu.");
        }
        catch (Exception ex)
        {
            _log.Info($"Lỗi tạo Restore Point: {ex.Message}");
            return new RestorePointResult(false, true, $"Không thể tạo điểm khôi phục: {ex.Message}. Tiếp tục áp dụng tối ưu.");
        }
    }
}
