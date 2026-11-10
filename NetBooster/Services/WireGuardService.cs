using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NetBooster.Services;

public sealed class WireGuardService
{
    private readonly ProcessRunner _runner; private readonly AppLogger _log;
    public WireGuardService(ProcessRunner runner, AppLogger log) { _runner = runner; _log = log; }
    public string? FindExecutable()
    {
        var paths = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WireGuard", "wireguard.exe") };
        return paths.FirstOrDefault(File.Exists);
    }
    public static string ValidateConfig(string path)
    {
        if (!File.Exists(path) || !path.EndsWith(".conf", StringComparison.OrdinalIgnoreCase)) return "Hãy chọn một tệp .conf có tồn tại.";
        if (new FileInfo(path).Length > 1024 * 1024) return "Tệp cấu hình quá lớn.";
        var text = File.ReadAllText(path);
        if (!Regex.IsMatch(text, @"(?mi)^\s*\[Interface\]\s*$") || !Regex.IsMatch(text, @"(?mi)^\s*PrivateKey\s*=\s*[^\s]+")) return "Thiếu [Interface] hoặc PrivateKey.";
        if (!Regex.IsMatch(text, @"(?mi)^\s*\[Peer\]\s*$") || !Regex.IsMatch(text, @"(?mi)^\s*PublicKey\s*=\s*[^\s]+")) return "Thiếu [Peer] hoặc PublicKey.";
        return "";
    }
    public async Task EnableAsync(string configPath)
    {
        var exe = FindExecutable() ?? throw new InvalidOperationException("Chưa cài WireGuard chính thức.");
        var validation = ValidateConfig(configPath); if (validation.Length > 0) throw new InvalidOperationException(validation);
        // Deliberately do not log the configuration path or its contents: it contains private material.
        var r = await _runner.RunAsync(exe, $"/installtunnelservice \"{configPath}\"", 30000);
        if (r.ExitCode != 0) throw new InvalidOperationException(r.StdErr);
        _log.Info("Đã yêu cầu bật tunnel WireGuard (không ghi khóa riêng vào nhật ký).");
    }
    public async Task DisableAsync(string tunnelName)
    {
        var exe = FindExecutable() ?? throw new InvalidOperationException("Chưa cài WireGuard chính thức.");
        if (!Regex.IsMatch(tunnelName, "^[A-Za-z0-9_-]{1,64}$")) throw new InvalidOperationException("Tên tunnel không hợp lệ.");
        var r = await _runner.RunAsync(exe, $"/uninstalltunnelservice \"{tunnelName}\""); if (r.ExitCode != 0) throw new InvalidOperationException(r.StdErr);
        _log.Info("Đã yêu cầu tắt tunnel WireGuard.");
    }
    public async Task<string> PublicIpAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        // This is only invoked from an explicit UI button.
        return (await http.GetStringAsync("https://api.ipify.org")).Trim();
    }
    public async Task<bool> DnsLeakCheckAsync()
    {
        var r = await _runner.PowerShellAsync("Resolve-DnsName -Name whoami.cloudflare -Server 1.1.1.1 -DnsOnly -ErrorAction Stop | Out-Null; 'OK'", 10000);
        return r.ExitCode == 0 && r.StdOut.Contains("OK");
    }
}
