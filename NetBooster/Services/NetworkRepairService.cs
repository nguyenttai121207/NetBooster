namespace NetBooster.Services;

public sealed class NetworkRepairService
{
    private readonly ProcessRunner _runner;
    private readonly AppLogger _log;

    public NetworkRepairService(ProcessRunner runner, AppLogger log) { _runner = runner; _log = log; }

    // Each method runs a specific repair action, returns a result message

    public async Task<string> RenewIpAsync()
    {
        // ipconfig /release then ipconfig /renew
        var release = await _runner.RunAsync("ipconfig.exe", "/release", 15000);
        var renew = await _runner.RunAsync("ipconfig.exe", "/renew", 30000);
        if (renew.ExitCode != 0) throw new InvalidOperationException("Không thể gia hạn IP: " + (string.IsNullOrWhiteSpace(renew.StdErr) ? renew.StdOut.Trim() : renew.StdErr.Trim()));
        _log.Info("Đã thực hiện Release/Renew IP.");
        return "Đã giải phóng và gia hạn địa chỉ IP thành công.";
    }

    public async Task<string> FlushDnsAsync()
    {
        var r = await _runner.RunAsync("ipconfig.exe", "/flushdns", 10000);
        if (r.ExitCode != 0) throw new InvalidOperationException("Không thể xóa DNS cache: " + (string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut.Trim() : r.StdErr.Trim()));
        _log.Info("Đã xóa DNS cache.");
        return "Đã xóa bộ nhớ đệm DNS thành công.";
    }

    public async Task<string> FlushArpAsync()
    {
        var r = await _runner.RunAsync("netsh.exe", "interface ip delete arpcache", 10000);
        // On newer Windows versions, arp -d may also work. netsh is more reliable.
        _log.Info("Đã xóa ARP cache.");
        return "Đã xóa bộ nhớ đệm ARP.";
    }

    public async Task<string> ResetWinsockAsync()
    {
        var r = await _runner.RunAsync("netsh.exe", "winsock reset", 15000);
        if (r.ExitCode != 0) throw new InvalidOperationException("Không thể reset Winsock: " + (string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut.Trim() : r.StdErr.Trim()));
        _log.Info("Đã reset Winsock catalog. Yêu cầu khởi động lại máy.");
        return "Đã reset Winsock catalog thành công. Cần khởi động lại máy tính để hoàn tất.";
    }

    public async Task<string> ResetTcpIpAsync()
    {
        var r = await _runner.RunAsync("netsh.exe", "int ip reset", 15000);
        if (r.ExitCode != 0) throw new InvalidOperationException("Không thể reset TCP/IP stack: " + (string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut.Trim() : r.StdErr.Trim()));
        _log.Info("Đã reset TCP/IP stack. Yêu cầu khởi động lại máy.");
        return "Đã reset TCP/IP stack thành công. Cần khởi động lại máy tính để hoàn tất.";
    }

    /// <summary>Returns predefined list of available repair actions with descriptions.</summary>
    public static IReadOnlyList<NetworkRepairAction> GetAvailableActions() => new NetworkRepairAction[]
    {
        new("renew-ip", "Gia hạn IP (Release/Renew)",
            "Giải phóng và yêu cầu cấp lại địa chỉ IP từ DHCP server.",
            "Khi không truy cập được mạng sau khi thay đổi cấu hình mạng hoặc khi IP bị xung đột.", false),
        new("flush-dns", "Xóa DNS Cache",
            "Xóa bộ nhớ đệm phân giải tên miền của Windows.",
            "Khi không truy cập được một số trang web nhưng ping IP trực tiếp vẫn được, hoặc sau khi đổi DNS.", false),
        new("flush-arp", "Xóa ARP Cache",
            "Xóa bảng ánh xạ IP-MAC address trong mạng nội bộ.",
            "Khi gặp lỗi kết nối đến các thiết bị trong mạng LAN sau khi thay đổi cấu hình mạng.", false),
        new("reset-winsock", "Reset Winsock Catalog",
            "Đặt lại catalog Winsock về mặc định. Có thể khắc phục lỗi do phần mềm bên thứ ba can thiệp vào mạng.",
            "Khi mạng không hoạt động sau khi gỡ VPN/proxy/antivirus, hoặc khi các ứng dụng không kết nối được Internet.", true),
        new("reset-tcpip", "Reset TCP/IP Stack",
            "Đặt lại toàn bộ ngăn xếp TCP/IP về mặc định gốc của Windows.",
            "Khi đã thử mọi cách khác mà vẫn không kết nối được mạng. Đây là biện pháp mạnh nhất.", true)
    };
}
