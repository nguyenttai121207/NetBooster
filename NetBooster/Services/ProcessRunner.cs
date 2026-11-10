using System.Diagnostics;

namespace NetBooster.Services;

public sealed class ProcessRunner
{
    public async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string fileName, string arguments, int timeoutMs = 30000)
    {
        using var p = new Process { StartInfo = new ProcessStartInfo(fileName, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 } };
        p.Start();
        var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        var exitTask = p.WaitForExitAsync();
        if (await Task.WhenAny(exitTask, Task.Delay(timeoutMs)) != exitTask) { try { p.Kill(true); } catch { } throw new TimeoutException($"Lệnh quá thời gian {timeoutMs / 1000} giây."); }
        await exitTask;
        return (p.ExitCode, await output, await error);
    }
    public Task<(int ExitCode, string StdOut, string StdErr)> PowerShellAsync(string script, int timeoutMs = 30000) =>
        RunAsync("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"", timeoutMs);
}
