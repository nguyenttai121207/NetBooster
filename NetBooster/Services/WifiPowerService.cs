using System.Text.Json;

namespace NetBooster.Services;

public sealed class WifiPowerService
{
    private readonly ProcessRunner _runner;
    private readonly AppLogger _log;
    private readonly string _backupPath = Path.Combine(AppPaths.CommonDataDirectory, "wifi-power-backup.json");

    public WifiPowerService(ProcessRunner runner, AppLogger log) { _runner = runner; _log = log; }

    public async Task<AdapterPowerState?> GetStateAsync(NetworkAdapterInfo adapter)
    {
        ValidateWifi(adapter);
        var r = await _runner.PowerShellAsync(QueryScript(adapter.InterfaceIndex));
        if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.StdOut)) return null;
        try
        {
            using var doc = JsonDocument.Parse(r.StdOut);
            var root = doc.RootElement;
            return new AdapterPowerState(Text(root, "SelectiveSuspend"), Text(root, "DeviceSleepOnDisconnect"), Text(root, "D0PacketCoalescing"), Text(root, "WakeOnMagicPacket"), Text(root, "WakeOnPattern"));
        }
        catch { return null; }
    }

    public async Task DisableSupportedSavingAsync(NetworkAdapterInfo adapter)
    {
        ValidateWifi(adapter);
        var current = await GetStateAsync(adapter) ?? throw new InvalidOperationException("Card này không cung cấp trạng thái Power Management qua Windows.");
        Directory.CreateDirectory(AppPaths.CommonDataDirectory);
        await File.WriteAllTextAsync(_backupPath, JsonSerializer.Serialize(new AdapterPowerBackup(adapter.InterfaceIndex, current, DateTime.UtcNow)));
        var r = await _runner.PowerShellAsync(DisableScript(adapter.InterfaceIndex));
        if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
        _log.Info($"Đã tắt các cờ tiết kiệm điện NetAdapter được hỗ trợ cho InterfaceIndex {adapter.InterfaceIndex}.");
    }

    public async Task RestoreAsync()
    {
        if (!File.Exists(_backupPath)) throw new InvalidOperationException("Chưa có bản sao lưu Power Management trong %ProgramData%\\NetBooster.");
        var backup = JsonSerializer.Deserialize<AdapterPowerBackup>(await File.ReadAllTextAsync(_backupPath)) ?? throw new InvalidOperationException("Bản sao lưu Power Management không hợp lệ.");
        if (backup.InterfaceIndex <= 0) throw new InvalidOperationException("Bản sao lưu có InterfaceIndex không hợp lệ.");
        var r = await _runner.PowerShellAsync(RestoreScript(backup));
        if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
        _log.Info($"Đã khôi phục Power Management cho InterfaceIndex {backup.InterfaceIndex}.");
    }

    private static string QueryScript(int id) => $@"
$a=Get-NetAdapter -InterfaceIndex {id} -ErrorAction Stop
$p=Get-NetAdapterPowerManagement -Name $a.Name -ErrorAction Stop
[pscustomobject]@{{SelectiveSuspend=[string]$p.SelectiveSuspend;DeviceSleepOnDisconnect=[string]$p.DeviceSleepOnDisconnect;D0PacketCoalescing=[string]$p.D0PacketCoalescing;WakeOnMagicPacket=[string]$p.WakeOnMagicPacket;WakeOnPattern=[string]$p.WakeOnPattern}} | ConvertTo-Json -Compress";

    private static string DisableScript(int id) => $@"
$a=Get-NetAdapter -InterfaceIndex {id} -ErrorAction Stop
$p=Get-NetAdapterPowerManagement -Name $a.Name -ErrorAction Stop
$params=@{{InputObject=$p;NoRestart=$true;ErrorAction='Stop'}}
if ([string]$p.SelectiveSuspend -in @('Enabled','Disabled')) {{$params['SelectiveSuspend']='Disabled'}}
if ([string]$p.DeviceSleepOnDisconnect -in @('Enabled','Disabled')) {{$params['DeviceSleepOnDisconnect']='Disabled'}}
if ([string]$p.D0PacketCoalescing -in @('Enabled','Disabled')) {{$params['D0PacketCoalescing']='Disabled'}}
if ($params.Count -eq 3) {{ throw 'Card không báo cờ tiết kiệm điện nào có thể đổi an toàn.' }}
Set-NetAdapterPowerManagement @params";

    private static string RestoreScript(AdapterPowerBackup backup)
    {
        var settings = new Dictionary<string, string?>
        {
            ["SelectiveSuspend"] = SafeSetting(backup.State.SelectiveSuspend),
            ["DeviceSleepOnDisconnect"] = SafeSetting(backup.State.DeviceSleepOnDisconnect),
            ["D0PacketCoalescing"] = SafeSetting(backup.State.D0PacketCoalescing),
            ["WakeOnMagicPacket"] = SafeSetting(backup.State.WakeOnMagicPacket),
            ["WakeOnPattern"] = SafeSetting(backup.State.WakeOnPattern)
        }.Where(x => x.Value is not null).ToArray();
        if (settings.Length == 0) throw new InvalidOperationException("Không có cờ Power Management hợp lệ để khôi phục.");
        var adds = string.Join(Environment.NewLine, settings.Select(x => "$params['" + x.Key + "']='" + x.Value + "'"));
        return $@"
$a=Get-NetAdapter -InterfaceIndex {backup.InterfaceIndex} -ErrorAction Stop
$p=Get-NetAdapterPowerManagement -Name $a.Name -ErrorAction Stop
$params=@{{InputObject=$p;NoRestart=$true;ErrorAction='Stop'}}
{adds}
Set-NetAdapterPowerManagement @params";
    }

    private static string? SafeSetting(string? value) => value is "Enabled" or "Disabled" ? value : null;
    private static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static string ErrorText((int ExitCode, string StdOut, string StdErr) r) => string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut.Trim() : r.StdErr.Trim();
    private static void ValidateWifi(NetworkAdapterInfo adapter)
    {
        if (!adapter.IsWifi || adapter.InterfaceIndex <= 0) throw new ArgumentException("Hãy chọn card Wi‑Fi hợp lệ từ danh sách.");
    }
}
