using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text.Json;

namespace NetBooster.Services;

public sealed class DnsService
{
    public static readonly IReadOnlyList<DnsProvider> PublicProviders = new[]
    {
        new DnsProvider("cloudflare", "Cloudflare", new[] { "1.1.1.1", "1.0.0.1" }, new[] { "2606:4700:4700::1111", "2606:4700:4700::1001" }),
        new DnsProvider("google", "Google Public DNS", new[] { "8.8.8.8", "8.8.4.4" }, new[] { "2001:4860:4860::8888", "2001:4860:4860::8844" }),
        new DnsProvider("quad9", "Quad9", new[] { "9.9.9.9", "149.112.112.112" }, new[] { "2620:fe::fe", "2620:fe::9" })
    };
    private readonly ProcessRunner _runner;
    private readonly AppLogger _log;
    private readonly string _backupPath;

    public DnsService(ProcessRunner runner, AppLogger log)
    {
        _runner = runner; _log = log;
        _backupPath = Path.Combine(AppPaths.CommonDataDirectory, "dns-backup.json");
    }

    public static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static void RestartAsAdministrator()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Không tìm thấy chương trình hiện tại.");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
    }

    public async Task<DnsProvider?> GetCurrentProviderAsync(int interfaceIndex)
    {
        EnsureKnownAdapter(interfaceIndex);
        var result = await _runner.PowerShellAsync($"$a=Get-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ErrorAction Stop; $a.ServerAddresses | ConvertTo-Json -Compress");
        if (result.ExitCode != 0) return null;
        var addresses = ReadAddresses(result.StdOut);
        if (addresses.Length == 0) return null;
        return new DnsProvider("current", "DNS hiện tại", addresses.Where(IsV4).ToArray(), addresses.Where(IsV6).ToArray());
    }

    public async Task<DnsBackup> BackupAsync(int interfaceIndex)
    {
        EnsureKnownAdapter(interfaceIndex);
        Directory.CreateDirectory(AppPaths.CommonDataDirectory);
        var script = $@"
$v4=Get-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -AddressFamily IPv4 -ErrorAction Stop
$v6=Get-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -AddressFamily IPv6 -ErrorAction Stop
$i4=Get-NetIPInterface -InterfaceIndex {interfaceIndex} -AddressFamily IPv4 -ErrorAction Stop
$i6=Get-NetIPInterface -InterfaceIndex {interfaceIndex} -AddressFamily IPv6 -ErrorAction Stop
[pscustomobject]@{{Ipv4=@($v4.ServerAddresses);Ipv6=@($v6.ServerAddresses);Ipv4Dhcp=($i4.Dhcp -eq 'Enabled');Ipv6Dhcp=($i6.Dhcp -eq 'Enabled')}} | ConvertTo-Json -Compress";
        var r = await _runner.PowerShellAsync(script);
        if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
        using var doc = JsonDocument.Parse(r.StdOut);
        var root = doc.RootElement;
        var backup = new DnsBackup(interfaceIndex,
            root.GetProperty("Ipv4Dhcp").GetBoolean(), root.GetProperty("Ipv6Dhcp").GetBoolean(),
            ReadAddresses(root.GetProperty("Ipv4")), ReadAddresses(root.GetProperty("Ipv6")), DateTime.UtcNow);
        await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(backup));
        _log.Info($"Đã sao lưu DNS của InterfaceIndex {interfaceIndex} vào ProgramData.");
        return backup;
    }

    public async Task ApplyAsync(int interfaceIndex, DnsProvider provider)
    {
        EnsureKnownAdapter(interfaceIndex);
        var addresses = provider.IPv4.Concat(provider.IPv6).Distinct().ToArray();
        if (addresses.Length == 0 || addresses.Any(x => !IPAddress.TryParse(x, out _))) throw new ArgumentException("Danh sách DNS không hợp lệ.");
        var backup = await BackupAsync(interfaceIndex);
        try
        {
            var array = "@(" + string.Join(',', addresses.Select(PsIpLiteral)) + ")";
            var r = await _runner.PowerShellAsync($"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ServerAddresses {array} -ErrorAction Stop; Clear-DnsClientCache -ErrorAction Stop");
            if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
            _log.Info($"Đã áp dụng DNS benchmark thắng: {provider.DisplayName} (InterfaceIndex {interfaceIndex}).");
        }
        catch
        {
            try { await RestoreAsync(backup); _log.Error("Áp dụng DNS lỗi; đã tự khôi phục bản sao lưu."); } catch (Exception restoreError) { _log.Error("Áp dụng DNS lỗi và tự khôi phục cũng lỗi: " + restoreError.Message); }
            throw;
        }
    }

    public async Task RestoreSavedAsync()
    {
        if (!File.Exists(_backupPath)) throw new InvalidOperationException("Chưa có bản sao lưu DNS trong %ProgramData%\\NetBooster.");
        var backup = JsonSerializer.Deserialize<DnsBackup>(await File.ReadAllTextAsync(_backupPath)) ?? throw new InvalidOperationException("Bản sao lưu DNS không hợp lệ.");
        EnsureKnownAdapter(backup.InterfaceIndex);
        await RestoreAsync(backup);
    }

    private async Task RestoreAsync(DnsBackup backup)
    {
        var valid4 = backup.Ipv4Addresses.Where(IsV4).ToArray();
        var valid6 = backup.Ipv6Addresses.Where(IsV6).ToArray();
        if (valid4.Length != backup.Ipv4Addresses.Length || valid6.Length != backup.Ipv6Addresses.Length) throw new InvalidOperationException("Bản sao lưu DNS chứa địa chỉ không hợp lệ.");
        var addresses = valid4.Concat(valid6).ToArray();
        var reset = backup.Ipv4Dhcp || backup.Ipv6Dhcp;
        var script = reset ? $"Set-DnsClientServerAddress -InterfaceIndex {backup.InterfaceIndex} -ResetServerAddresses -ErrorAction Stop; " : string.Empty;
        if (addresses.Length > 0) script += $"Set-DnsClientServerAddress -InterfaceIndex {backup.InterfaceIndex} -ServerAddresses @({string.Join(',', addresses.Select(PsIpLiteral))}) -ErrorAction Stop; ";
        script += "Clear-DnsClientCache -ErrorAction Stop";
        var r = await _runner.PowerShellAsync(script);
        if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
        _log.Info($"Đã khôi phục DNS cho InterfaceIndex {backup.InterfaceIndex}.");
    }

    public async Task ClearCacheAsync()
    {
        var r = await _runner.PowerShellAsync("Clear-DnsClientCache -ErrorAction Stop");
        if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
        _log.Info("Đã xóa cache DNS.");
    }

    public async Task<DnsProviderBenchmark> MeasureProviderAsync(DnsProvider provider, IReadOnlyList<string> domains, CancellationToken cancellationToken = default)
    {
        var validDomains = domains.Where(IsSafeDomain).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (validDomains.Length < 6) throw new ArgumentException("Cần ít nhất 6 tên miền hợp lệ để benchmark DNS.");
        var measurements = new List<DnsMeasurement>();
        foreach (var state in new[] { CacheState.Cold, CacheState.Hot })
        {
            if (state == CacheState.Cold) await TryClearCacheForBenchmarkAsync();
            var servers = provider.IPv4.Concat(HasUsableIpv6() ? provider.IPv6 : Array.Empty<string>()).Distinct();
            foreach (var server in servers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IPAddress.TryParse(server, out var ip)) continue;
                measurements.Add(await MeasureServerAsync(ip, validDomains, state, cancellationToken));
            }
        }
        return new DnsProviderBenchmark(provider, measurements);
    }

    private async Task TryClearCacheForBenchmarkAsync()
    {
        // This is a best effort only. Direct -Server measurements may still hit the resolver's own cache; UI states this limitation.
        await _runner.PowerShellAsync("Clear-DnsClientCache -ErrorAction SilentlyContinue", 10_000);
    }

    private async Task<DnsMeasurement> MeasureServerAsync(IPAddress server, IReadOnlyList<string> domains, CacheState state, CancellationToken cancellationToken)
    {
        var samples = new List<double>(); var errors = 0;
        foreach (var domain in domains)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var script = $"$sw=[Diagnostics.Stopwatch]::StartNew(); try {{ Resolve-DnsName -Name '{domain}' -Server '{server}' -Type A -DnsOnly -NoHostsFile -ErrorAction Stop | Out-Null; [Console]::Write($sw.Elapsed.TotalMilliseconds.ToString([Globalization.CultureInfo]::InvariantCulture)) }} catch {{ [Console]::Write('ERR') }}";
            var r = await _runner.PowerShellAsync(script, 10_000);
            if (r.ExitCode != 0 || !double.TryParse(r.StdOut.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ms)) errors++; else samples.Add(ms);
        }
        var median = samples.Count == 0 ? -1 : samples.OrderBy(x => x).ElementAt(samples.Count / 2);
        return new DnsMeasurement(server.ToString(), server.AddressFamily, state, median, (errors * 100d) / domains.Count, domains.Count);
    }

    private static string[] ReadAddresses(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { using var doc = JsonDocument.Parse(json); return ReadAddresses(doc.RootElement); } catch { return Array.Empty<string>(); }
    }
    private static string[] ReadAddresses(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => IPAddress.TryParse(x, out _)).ToArray(),
        JsonValueKind.String when IPAddress.TryParse(element.GetString(), out _) => new[] { element.GetString()! },
        _ => Array.Empty<string>()
    };
    private static bool IsV4(string value) => IPAddress.TryParse(value, out var x) && x.AddressFamily == AddressFamily.InterNetwork;
    private static bool IsV6(string value) => IPAddress.TryParse(value, out var x) && x.AddressFamily == AddressFamily.InterNetworkV6;
    private static bool HasUsableIpv6() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Any(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal && !a.Address.IsIPv6Loopback);
    private static string PsIpLiteral(string ip) => "'" + ip + "'"; // called only after IPAddress.TryParse.
    private static bool IsSafeDomain(string domain) => System.Text.RegularExpressions.Regex.IsMatch(domain, "^(?=.{1,253}$)([A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\\.)+[A-Za-z]{2,63}$");
    private static string ErrorText((int ExitCode, string StdOut, string StdErr) r) => string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut.Trim() : r.StdErr.Trim();
    private static void EnsureKnownAdapter(int interfaceIndex)
    {
        if (interfaceIndex <= 0 || !NetworkInterface.GetAllNetworkInterfaces().Any(n => TryIndex(n) == interfaceIndex)) throw new ArgumentException("Card mạng đã chọn không còn tồn tại.");
    }
    private static int TryIndex(NetworkInterface n) { try { return n.GetIPProperties().GetIPv4Properties().Index; } catch { return -1; } }
}
