using System.Diagnostics;
using System.Text.Json;

namespace NetBooster.Services;

public sealed class NetworkUsageService
{
    private readonly ProcessRunner _runner;
    private readonly AppLogger _log;
    public NetworkUsageService(ProcessRunner runner, AppLogger log) { _runner = runner; _log = log; }

    public async Task<IReadOnlyList<NetworkProcessActivity>> GetActiveTcpProcessesAsync()
    {
        // Get-NetTCPConnection does not expose per-process byte rates. We therefore report active TCP connection counts,
        // which is an honest indicator for investigation rather than a fabricated bandwidth number.
        const string script = "Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue | Group-Object OwningProcess | ForEach-Object { [pscustomobject]@{ ProcessId=[int]$_.Name; Connections=$_.Count } } | ConvertTo-Json -Compress";
        var r = await _runner.PowerShellAsync(script);
        if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.StdOut)) return Array.Empty<NetworkProcessActivity>();
        try
        {
            using var doc = JsonDocument.Parse(r.StdOut);
            var elements = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray() : new[] { doc.RootElement }.AsEnumerable();
            var now = DateTime.UtcNow;
            return elements.Select(item =>
            {
                var id = item.GetProperty("ProcessId").GetInt32();
                var connections = item.GetProperty("Connections").GetInt32();
                return new NetworkProcessActivity(id, ProcessName(id), connections, now);
            }).Where(x => x.ProcessId > 0).OrderByDescending(x => x.ActiveTcpConnections).ThenBy(x => x.ProcessName).ToArray();
        }
        catch { return Array.Empty<NetworkProcessActivity>(); }
    }

    public Task CloseChosenProcessAsync(int processId)
    {
        if (processId <= 4 || processId == Environment.ProcessId) throw new ArgumentException("Không thể đóng tiến trình hệ thống hoặc chính NetBooster.");
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            _log.Info($"Người dùng đã yêu cầu đóng tiến trình PID {processId}.");
            return Task.CompletedTask;
        }
        catch (Exception ex) { throw new InvalidOperationException("Không thể đóng tiến trình đã chọn: " + ex.Message, ex); }
    }

    private static string ProcessName(int processId)
    {
        try { using var p = Process.GetProcessById(processId); return p.ProcessName; }
        catch { return "(không còn tồn tại/không có quyền đọc)"; }
    }
}
