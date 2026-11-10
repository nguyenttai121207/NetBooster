using System.Text.Json;

namespace NetBooster.Services;

/// <summary>
/// Delivery Optimization service to read and limit Windows Update background bandwidth.
/// Uses official Microsoft cmdlets / Group Policy settings with automatic backup and rollback.
/// </summary>
public sealed class DeliveryOptimizationService
{
    private readonly ProcessRunner _runner;
    private readonly AppLogger _log;
    private readonly string _backupPath = Path.Combine(AppPaths.BackupDirectory, "delivery-optimization-backup.json");

    public DeliveryOptimizationService(ProcessRunner runner, AppLogger log)
    {
        _runner = runner;
        _log = log;
    }

    public async Task<DeliveryOptimizationState> GetStateAsync()
    {
        var script = @"
if (Get-Command Get-DOPercentageMaxBackgroundBandwidth -ErrorAction SilentlyContinue) {
    try {
        $val = Get-DOPercentageMaxBackgroundBandwidth -ErrorAction Stop
        if ($val -ne $null -and $val -ge 0 -and $val -le 100) {
            Write-Output ""CMDLET:$val""
        } else {
            Write-Output ""CMDLET:NONE""
        }
    } catch {
        Write-Output ""CMDLET:NONE""
    }
} else {
    # Check Registry Policy
    $regPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization'
    if (Test-Path $regPath) {
        $regVal = (Get-ItemProperty -Path $regPath -Name 'DOMaxBackgroundBandwidth' -ErrorAction SilentlyContinue).DOMaxBackgroundBandwidth
        if ($regVal -ne $null) {
            Write-Output ""REG:$regVal""
        } else {
            Write-Output ""UNSUPPORTED""
        }
    } else {
        Write-Output ""UNSUPPORTED""
    }
}";
        var r = await _runner.PowerShellAsync(script);
        var outText = r.StdOut.Trim();

        if (outText.StartsWith("CMDLET:") || outText.StartsWith("REG:"))
        {
            var parts = outText.Split(':');
            if (parts.Length > 1 && int.TryParse(parts[1], out var percent) && percent >= 0 && percent <= 100)
            {
                return new DeliveryOptimizationState(true, percent, $"Đang giới hạn băng thông nền ở {percent}%.");
            }
            return new DeliveryOptimizationState(true, null, "Không có giới hạn băng thông nền đang áp dụng.");
        }

        return new DeliveryOptimizationState(false, null, "Windows không hỗ trợ cmdlet hoặc chính sách Delivery Optimization.");
    }

    public async Task LimitBackgroundTo10PercentAsync()
    {
        var state = await GetStateAsync();
        if (!state.IsSupported) throw new NotSupportedException("Hệ điều hành không hỗ trợ cấu hình Delivery Optimization.");

        // Backup current state to %ProgramData%\NetBooster\backups\
        var backup = new DeliveryOptimizationBackup(state.BackgroundPercent, DateTime.UtcNow);
        await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(backup));

        // Apply via Cmdlet or Registry Policy
        var applyScript = @"
$success = $false
if (Get-Command Set-DOPercentageMaxBackgroundBandwidth -ErrorAction SilentlyContinue) {
    try {
        Set-DOPercentageMaxBackgroundBandwidth -MaxBackgroundBandwidth 10 -ErrorAction Stop
        $success = $true
    } catch { }
}
if (-not $success) {
    # Apply official Group Policy registry key
    $regPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization'
    if (-not (Test-Path $regPath)) { New-Item -Path $regPath -Force | Out-Null }
    Set-ItemProperty -Path $regPath -Name 'DOMaxBackgroundBandwidth' -Value 10 -Type DWord -Force
    $success = $true
}
if ($success) { 'OK' } else { throw 'Không thể thiết lập giới hạn Delivery Optimization.' }";

        try
        {
            var r = await _runner.PowerShellAsync(applyScript);
            if (r.ExitCode != 0 || !r.StdOut.Contains("OK"))
            {
                throw new InvalidOperationException(ErrorText(r));
            }
            _log.Info("Đã đặt giới hạn Delivery Optimization nền 10%.");
        }
        catch
        {
            // Automatic immediate rollback on error
            try { await RestoreAsync(); } catch { }
            throw;
        }
    }

    public async Task RestoreAsync()
    {
        if (!File.Exists(_backupPath)) throw new InvalidOperationException("Chưa có bản sao lưu Delivery Optimization trong %ProgramData%\\NetBooster\\backups.");
        var backup = JsonSerializer.Deserialize<DeliveryOptimizationBackup>(await File.ReadAllTextAsync(_backupPath))
            ?? throw new InvalidOperationException("Bản sao lưu Delivery Optimization không hợp lệ.");

        var restoreScript = @"
$val = " + (backup.BackgroundPercent.HasValue ? backup.BackgroundPercent.Value.ToString() : "$null") + @"
if (Get-Command Set-DOPercentageMaxBackgroundBandwidth -ErrorAction SilentlyContinue) {
    try {
        if ($val -ne $null) {
            Set-DOPercentageMaxBackgroundBandwidth -MaxBackgroundBandwidth $val -ErrorAction Stop
        } else {
            Set-DOPercentageMaxBackgroundBandwidth -Reset -ErrorAction Stop
        }
    } catch { }
}
# Reset Registry
$regPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization'
if (Test-Path $regPath) {
    if ($val -ne $null) {
        Set-ItemProperty -Path $regPath -Name 'DOMaxBackgroundBandwidth' -Value $val -Type DWord -Force
    } else {
        Remove-ItemProperty -Path $regPath -Name 'DOMaxBackgroundBandwidth' -ErrorAction SilentlyContinue
    }
}
'OK'";

        var r = await _runner.PowerShellAsync(restoreScript);
        if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
        _log.Info("Đã khôi phục cài đặt Delivery Optimization.");
    }

    private static string ErrorText((int ExitCode, string StdOut, string StdErr) r) =>
        string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut.Trim() : r.StdErr.Trim();
}
