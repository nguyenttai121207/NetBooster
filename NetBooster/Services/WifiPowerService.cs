using System.Text.Json;

namespace NetBooster.Services;

/// <summary>
/// Manages Wi-Fi adapter power saving flags and advanced driver properties (Preferred Band, Roaming Aggressiveness).
/// Uses official PowerShell cmdlets Get/Set-NetAdapterPowerManagement and Set-NetAdapterAdvancedProperty.
/// Identifies adapter by NetworkInterface.Id (NetCfgInstanceId / InterfaceGuid) and ignores virtual cards.
/// Never touches registry PnPCapabilities.
/// </summary>
public sealed class WifiPowerService
{
    private readonly ProcessRunner _runner;
    private readonly AppLogger _log;
    private readonly string _powerBackupPath = Path.Combine(AppPaths.BackupDirectory, "wifi-power-backup.json");
    private readonly string _driverBackupPath = Path.Combine(AppPaths.BackupDirectory, "wifi-driver-backup.json");

    public WifiPowerService(ProcessRunner runner, AppLogger log)
    {
        _runner = runner;
        _log = log;
    }

    public async Task<AdapterPowerState?> GetPowerStateAsync(NetworkAdapterInfo adapter)
    {
        ValidateWifi(adapter);
        var script = $@"
$guid = '{adapter.Id}'
$a = Get-NetAdapter | Where-Object {{ ($_.InterfaceGuid -eq $guid -or $_.NetCfgInstanceId -eq $guid -or $_.InterfaceGuid -like ""*$guid*"") -and -not $_.Virtual -and $_.InterfaceDescription -notmatch 'VMware|VirtualBox|Hyper-V|TAP|VPN|WireGuard|Virtual|Loopback|Npcap|WSL' }} | Select-Object -First 1
if (-not $a) {{ return }}
$p = Get-NetAdapterPowerManagement -Name $a.Name -ErrorAction SilentlyContinue
if ($p) {{
    [pscustomobject]@{{
        SelectiveSuspend = [string]$p.SelectiveSuspend
        DeviceSleepOnDisconnect = [string]$p.DeviceSleepOnDisconnect
        D0PacketCoalescing = [string]$p.D0PacketCoalescing
        WakeOnMagicPacket = [string]$p.WakeOnMagicPacket
        WakeOnPattern = [string]$p.WakeOnPattern
    }} | ConvertTo-Json -Compress
}}";
        var r = await _runner.PowerShellAsync(script);
        if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.StdOut)) return null;
        try
        {
            using var doc = JsonDocument.Parse(r.StdOut);
            var root = doc.RootElement;
            return new AdapterPowerState(
                Text(root, "SelectiveSuspend"),
                Text(root, "DeviceSleepOnDisconnect"),
                Text(root, "D0PacketCoalescing"),
                Text(root, "WakeOnMagicPacket"),
                Text(root, "WakeOnPattern"));
        }
        catch { return null; }
    }

    public async Task DisableSupportedSavingAsync(NetworkAdapterInfo adapter)
    {
        ValidateWifi(adapter);
        var current = await GetPowerStateAsync(adapter) ?? throw new InvalidOperationException("Card này không cung cấp trạng thái Power Management qua Windows hoặc là card ảo.");
        
        Directory.CreateDirectory(AppPaths.CommonDataDirectory);
        await File.WriteAllTextAsync(_powerBackupPath, JsonSerializer.Serialize(new AdapterPowerBackup(adapter.Id, current, DateTime.UtcNow)));

        var script = $@"
$guid = '{adapter.Id}'
$a = Get-NetAdapter | Where-Object {{ ($_.InterfaceGuid -eq $guid -or $_.NetCfgInstanceId -eq $guid -or $_.InterfaceGuid -like ""*$guid*"") -and -not $_.Virtual -and $_.InterfaceDescription -notmatch 'VMware|VirtualBox|Hyper-V|TAP|VPN|WireGuard|Virtual|Loopback|Npcap|WSL' }} | Select-Object -First 1
if (-not $a) {{ throw 'Không tìm thấy card mạng vật lý phù hợp.' }}
$p = Get-NetAdapterPowerManagement -Name $a.Name -ErrorAction Stop
$params = @{{ InputObject = $p; NoRestart = $true; ErrorAction = 'Stop' }}
if ([string]$p.SelectiveSuspend -in @('Enabled','Disabled')) {{ $params['SelectiveSuspend'] = 'Disabled' }}
if ([string]$p.DeviceSleepOnDisconnect -in @('Enabled','Disabled')) {{ $params['DeviceSleepOnDisconnect'] = 'Disabled' }}
if ([string]$p.D0PacketCoalescing -in @('Enabled','Disabled')) {{ $params['D0PacketCoalescing'] = 'Disabled' }}
if ($params.Count -le 2) {{ throw 'Card không báo cờ tiết kiệm điện nào có thể đổi an toàn.' }}
Set-NetAdapterPowerManagement @params";

        var r = await _runner.PowerShellAsync(script);
        if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
        _log.Info($"Đã tắt các cờ tiết kiệm điện NetAdapter cho {adapter.Name} ({adapter.Id}).");
    }

    public async Task RestorePowerAsync()
    {
        if (!File.Exists(_powerBackupPath)) throw new InvalidOperationException("Chưa có bản sao lưu Power Management trong %ProgramData%\\NetBooster.");
        var backup = JsonSerializer.Deserialize<AdapterPowerBackup>(await File.ReadAllTextAsync(_powerBackupPath)) ?? throw new InvalidOperationException("Bản sao lưu Power Management không hợp lệ.");
        if (string.IsNullOrWhiteSpace(backup.InterfaceGuid)) throw new InvalidOperationException("Bản sao lưu thiếu InterfaceGuid.");

        var settings = new Dictionary<string, string?>
        {
            ["SelectiveSuspend"] = SafeSetting(backup.State.SelectiveSuspend),
            ["DeviceSleepOnDisconnect"] = SafeSetting(backup.State.DeviceSleepOnDisconnect),
            ["D0PacketCoalescing"] = SafeSetting(backup.State.D0PacketCoalescing),
            ["WakeOnMagicPacket"] = SafeSetting(backup.State.WakeOnMagicPacket),
            ["WakeOnPattern"] = SafeSetting(backup.State.WakeOnPattern)
        }.Where(x => x.Value is not null).ToArray();

        if (settings.Length == 0) throw new InvalidOperationException("Không có cờ Power Management hợp lệ để khôi phục.");
        var adds = string.Join(Environment.NewLine, settings.Select(x => $"$params['{x.Key}']='{x.Value}'"));

        var script = $@"
$guid = '{backup.InterfaceGuid}'
$a = Get-NetAdapter | Where-Object {{ ($_.InterfaceGuid -eq $guid -or $_.NetCfgInstanceId -eq $guid -or $_.InterfaceGuid -like ""*$guid*"") -and -not $_.Virtual -and $_.InterfaceDescription -notmatch 'VMware|VirtualBox|Hyper-V|TAP|VPN|WireGuard|Virtual|Loopback|Npcap|WSL' }} | Select-Object -First 1
if (-not $a) {{ throw 'Không tìm thấy card mạng ban đầu.' }}
$p = Get-NetAdapterPowerManagement -Name $a.Name -ErrorAction Stop
$params = @{{ InputObject = $p; NoRestart = $true; ErrorAction = 'Stop' }}
{adds}
Set-NetAdapterPowerManagement @params";

        var r = await _runner.PowerShellAsync(script);
        if (r.ExitCode != 0) throw new InvalidOperationException(ErrorText(r));
        _log.Info($"Đã khôi phục Power Management cho {backup.InterfaceGuid}.");
    }

    // --- Driver Advanced Properties (Preferred Band, Roaming Aggressiveness) ---

    public async Task<IReadOnlyList<WifiDriverProperty>> GetAdvancedPropertiesAsync(NetworkAdapterInfo adapter)
    {
        ValidateWifi(adapter);
        var script = $@"
$guid = '{adapter.Id}'
$a = Get-NetAdapter | Where-Object {{ ($_.InterfaceGuid -eq $guid -or $_.NetCfgInstanceId -eq $guid -or $_.InterfaceGuid -like ""*$guid*"") -and -not $_.Virtual -and $_.InterfaceDescription -notmatch 'VMware|VirtualBox|Hyper-V|TAP|VPN|WireGuard|Virtual|Loopback|Npcap|WSL' }} | Select-Object -First 1
if (-not $a) {{ return '[]' }}
$props = Get-NetAdapterAdvancedProperty -Name $a.Name -ErrorAction SilentlyContinue | Where-Object {{
    $_.DisplayName -match 'Preferred Band|Roaming Aggressiveness' -or $_.RegistryKeyword -match 'RoamingPreferredBandType|RoamAggressiveness'
}}
if (-not $props) {{ return '[]' }}
@($props) | ForEach-Object {{
    [pscustomobject]@{{
        DisplayName = [string]$_.DisplayName
        DisplayValue = [string]$_.DisplayValue
        RegistryKeyword = [string]$_.RegistryKeyword
        ValidValues = @($_.ValidDisplayValues)
    }}
}} | ConvertTo-Json -Compress";

        var r = await _runner.PowerShellAsync(script);
        if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.StdOut)) return Array.Empty<WifiDriverProperty>();

        try
        {
            using var doc = JsonDocument.Parse(r.StdOut);
            var list = new List<WifiDriverProperty>();
            var elements = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray() : new[] { doc.RootElement }.AsEnumerable();
            foreach (var el in elements)
            {
                var displayName = Text(el, "DisplayName") ?? "";
                var displayValue = Text(el, "DisplayValue") ?? "";
                var regKeyword = Text(el, "RegistryKeyword") ?? "";
                var validVals = new List<string>();
                if (el.TryGetProperty("ValidValues", out var valProp) && valProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in valProp.EnumerateArray())
                        if (v.GetString() is { } s) validVals.Add(s);
                }
                if (!string.IsNullOrWhiteSpace(displayName))
                    list.Add(new WifiDriverProperty(displayName, displayValue, validVals, regKeyword));
            }
            return list;
        }
        catch { return Array.Empty<WifiDriverProperty>(); }
    }

    public async Task OptimizeAdvancedPropertiesAsync(NetworkAdapterInfo adapter)
    {
        ValidateWifi(adapter);
        var currentProps = await GetAdvancedPropertiesAsync(adapter);
        if (currentProps.Count == 0) return;

        // Backup current values
        var backupDict = currentProps.ToDictionary(p => p.DisplayName, p => p.CurrentValue);
        await File.WriteAllTextAsync(_driverBackupPath, JsonSerializer.Serialize(new WifiDriverBackup(adapter.Id, backupDict, DateTime.UtcNow)));

        foreach (var prop in currentProps)
        {
            string? targetValue = null;

            // Preferred Band: Prefer 5GHz or 5GHz+6GHz if available
            if (prop.DisplayName.Contains("Preferred Band", StringComparison.OrdinalIgnoreCase) ||
                prop.RegistryKeyword.Contains("RoamingPreferredBandType", StringComparison.OrdinalIgnoreCase))
            {
                targetValue = prop.ValidValues.FirstOrDefault(v => v.Contains("Prefer 5GHz + 6GHz", StringComparison.OrdinalIgnoreCase))
                    ?? prop.ValidValues.FirstOrDefault(v => v.Contains("Prefer 5GHz", StringComparison.OrdinalIgnoreCase));
            }
            // Roaming Aggressiveness: Set to Medium-low or Lowest to prevent lag spikes while stationary
            else if (prop.DisplayName.Contains("Roaming Aggressiveness", StringComparison.OrdinalIgnoreCase) ||
                     prop.RegistryKeyword.Contains("RoamAggressiveness", StringComparison.OrdinalIgnoreCase))
            {
                targetValue = prop.ValidValues.FirstOrDefault(v => v.Contains("Medium-low", StringComparison.OrdinalIgnoreCase))
                    ?? prop.ValidValues.FirstOrDefault(v => v.Contains("Lowest", StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(targetValue) && targetValue != prop.CurrentValue)
            {
                var script = $@"
$guid = '{adapter.Id}'
$a = Get-NetAdapter | Where-Object {{ ($_.InterfaceGuid -eq $guid -or $_.NetCfgInstanceId -eq $guid -or $_.InterfaceGuid -like ""*$guid*"") -and -not $_.Virtual }} | Select-Object -First 1
if ($a) {{
    Set-NetAdapterAdvancedProperty -Name $a.Name -DisplayName '{prop.DisplayName}' -DisplayValue '{targetValue}' -NoRestart -ErrorAction Stop
}}";
                var r = await _runner.PowerShellAsync(script);
                if (r.ExitCode == 0)
                {
                    _log.Info($"Đã chỉnh thuộc tính nâng cao {prop.DisplayName} -> {targetValue} cho {adapter.Name}.");
                }
            }
        }
    }

    public async Task RestoreAdvancedPropertiesAsync()
    {
        if (!File.Exists(_driverBackupPath)) throw new InvalidOperationException("Chưa có bản sao lưu thuộc tính driver trong %ProgramData%\\NetBooster.");
        var backup = JsonSerializer.Deserialize<WifiDriverBackup>(await File.ReadAllTextAsync(_driverBackupPath)) ?? throw new InvalidOperationException("Bản sao lưu không hợp lệ.");

        foreach (var (propName, propValue) in backup.SavedValues)
        {
            var script = $@"
$guid = '{backup.InterfaceGuid}'
$a = Get-NetAdapter | Where-Object {{ ($_.InterfaceGuid -eq $guid -or $_.NetCfgInstanceId -eq $guid -or $_.InterfaceGuid -like ""*$guid*"") -and -not $_.Virtual }} | Select-Object -First 1
if ($a) {{
    Set-NetAdapterAdvancedProperty -Name $a.Name -DisplayName '{propName}' -DisplayValue '{propValue}' -NoRestart -ErrorAction SilentlyContinue
}}";
            await _runner.PowerShellAsync(script);
        }
        _log.Info($"Đã khôi phục thuộc tính nâng cao của driver cho {backup.InterfaceGuid}.");
    }

    private static string? SafeSetting(string? value) => value is "Enabled" or "Disabled" ? value : null;
    private static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static string ErrorText((int ExitCode, string StdOut, string StdErr) r) => string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut.Trim() : r.StdErr.Trim();
    private static void ValidateWifi(NetworkAdapterInfo adapter)
    {
        if (!adapter.IsWifi || string.IsNullOrWhiteSpace(adapter.Id)) throw new ArgumentException("Hãy chọn card Wi‑Fi hợp lệ.");
    }
}
