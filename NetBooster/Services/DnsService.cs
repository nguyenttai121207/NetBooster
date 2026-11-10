using System.Security.Principal;
using System.Text.Json;

namespace NetBooster.Services;

public sealed class DnsService
{
    private readonly ProcessRunner _runner;
    private readonly AppLogger _log;
    private readonly string _backupPath;
    public DnsService(ProcessRunner runner, AppLogger log)
    {
        _runner = runner; _log = log;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetBooster"); Directory.CreateDirectory(dir);
        _backupPath = Path.Combine(dir, "dns-backup.json");
    }
    public static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static void RestartAsAdministrator()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Không tìm thấy chương trình hiện tại.");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
    }
    public async Task<string[]> GetActiveAdaptersAsync()
    {
        var r = await _runner.PowerShellAsync("Get-NetAdapter | Where-Object Status -eq 'Up' | Select-Object -ExpandProperty Name");
        return r.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
    public async Task<string[]> GetServersAsync(string adapter)
    {
        var r = await _runner.PowerShellAsync($"(Get-DnsClientServerAddress -InterfaceAlias {PsLiteral(adapter)} -AddressFamily IPv4).ServerAddresses");
        if (r.ExitCode != 0) return Array.Empty<string>();
        return r.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
    public async Task<DnsBackup> BackupAsync(string adapter)
    {
        // The adapter name is passed as a PowerShell single-quoted literal after escaping apostrophes.
        var a = PsLiteral(adapter);
        var r = await _runner.PowerShellAsync($"$x=Get-DnsClientServerAddress -InterfaceAlias {a} -AddressFamily IPv4; $i=Get-NetIPInterface -InterfaceAlias {a} -AddressFamily IPv4; [pscustomobject]@{{Addresses=@($x.ServerAddresses); IsDhcp=($i.Dhcp -eq 'Enabled')}} | ConvertTo-Json -Compress");
        if (r.ExitCode != 0) throw new InvalidOperationException(r.StdErr);
        using var doc = JsonDocument.Parse(r.StdOut);
        var root = doc.RootElement;
        var addresses = root.GetProperty("Addresses").EnumerateArray().Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var backup = new DnsBackup(adapter, root.GetProperty("IsDhcp").GetBoolean(), addresses, DateTime.UtcNow);
        await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(backup));
        _log.Info($"Đã sao lưu DNS của bộ điều hợp: {adapter}"); return backup;
    }
    public async Task ApplyAsync(string adapter, string[] servers, bool enableDoh)
    {
        var backup = await BackupAsync(adapter);
        try
        {
            var a = PsLiteral(adapter); var array = "@(" + string.Join(',', servers.Select(PsLiteral)) + ")";
            var r = await _runner.PowerShellAsync($"Set-DnsClientServerAddress -InterfaceAlias {a} -ServerAddresses {array}; Clear-DnsClientCache");
            if (r.ExitCode != 0) throw new InvalidOperationException(r.StdErr);
            if (enableDoh)
            {
                // Windows may not support this cmdlet or a provider may have no template; failure here rolls DNS back.
                foreach (var server in servers)
                {
                    var doh = await _runner.PowerShellAsync($"if (Get-Command Add-DnsClientDohServerAddress -ErrorAction SilentlyContinue) {{ Add-DnsClientDohServerAddress -ServerAddress {PsLiteral(server)} -AllowFallbackToUdp $true -AutoUpgrade $true -ErrorAction Stop }} else {{ throw 'Windows không hỗ trợ DNS-over-HTTPS cmdlet.' }}");
                    if (doh.ExitCode != 0) throw new InvalidOperationException(doh.StdErr);
                }
            }
            _log.Info($"Đã áp dụng DNS mới cho {adapter}: {string.Join(", ", servers)}");
        }
        catch
        {
            await RestoreAsync(backup); _log.Error("Áp dụng DNS lỗi; đã tự khôi phục bản sao lưu."); throw;
        }
    }
    public async Task RestoreSavedAsync()
    {
        if (!File.Exists(_backupPath)) throw new InvalidOperationException("Chưa có bản sao lưu DNS để khôi phục.");
        var backup = JsonSerializer.Deserialize<DnsBackup>(await File.ReadAllTextAsync(_backupPath)) ?? throw new InvalidOperationException("Bản sao lưu DNS không hợp lệ.");
        await RestoreAsync(backup);
    }
    private async Task RestoreAsync(DnsBackup b)
    {
        var a = PsLiteral(b.AdapterName);
        string script = b.IsDhcp || b.Addresses.Length == 0
            ? $"Set-DnsClientServerAddress -InterfaceAlias {a} -ResetServerAddresses; Clear-DnsClientCache"
            : $"Set-DnsClientServerAddress -InterfaceAlias {a} -ServerAddresses @({string.Join(',', b.Addresses.Select(PsLiteral))}); Clear-DnsClientCache";
        var r = await _runner.PowerShellAsync(script);
        if (r.ExitCode != 0) throw new InvalidOperationException(r.StdErr);
        _log.Info($"Đã khôi phục DNS cho {b.AdapterName}");
    }
    public async Task ClearCacheAsync()
    {
        var r = await _runner.PowerShellAsync("Clear-DnsClientCache"); if (r.ExitCode != 0) throw new InvalidOperationException(r.StdErr);
        _log.Info("Đã xóa cache DNS.");
    }
    public async Task<DnsMeasurement> MeasureAsync(string server, IEnumerable<string> domains, int repeats = 3)
    {
        var values = new List<double>(); var failures = 0;
        foreach (var domain in domains)
        for (var i = 0; i < repeats; i++)
        {
            var script = $"$sw=[Diagnostics.Stopwatch]::StartNew(); try {{ Resolve-DnsName -Name {PsLiteral(domain)} -Server {PsLiteral(server)} -DnsOnly -ErrorAction Stop | Out-Null; $sw.Elapsed.TotalMilliseconds }} catch {{ 'ERR' }}";
            var r = await _runner.PowerShellAsync(script, 10000);
            if (r.ExitCode != 0 || r.StdOut.Trim().Equals("ERR", StringComparison.OrdinalIgnoreCase) || !double.TryParse(r.StdOut.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ms)) failures++; else values.Add(ms);
        }
        return new DnsMeasurement(server, values.Count == 0 ? -1 : values.OrderBy(x => x).ElementAt(values.Count / 2), 100d * failures / (failures + values.Count), failures + values.Count);
    }
    private static string PsLiteral(string text) => "'" + text.Replace("'", "''") + "'";
}
