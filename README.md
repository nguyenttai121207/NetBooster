# NetBooster

Ứng dụng Windows 10/11 một cửa sổ để đo chất lượng mạng, benchmark DNS và điều khiển tunnel WireGuard do chính bạn cung cấp. NetBooster **không tăng băng thông Internet và không thể vượt gói cước**. Nó không có telemetry, quảng cáo, dịch vụ nền hay tự khởi động.

## Kiến trúc

Chọn **C# .NET 8 WinForms**: có sẵn Windows Desktop Runtime, nhẹ hơn một ứng dụng web/Electron, gọi được API .NET/PowerShell gốc, và có thể publish thành một `.exe` độc lập. Mã tách theo:

- `Services/NetworkDiagnostics.cs`: ping gateway/Internet, DNS hiện tại, thông tin `netsh wlan` và kết luận thuần logic.
- `Services/DnsService.cs`: đo `Resolve-DnsName`, sao lưu JSON cục bộ, áp dụng/khôi phục DNS với xử lý rollback.
- `Services/WireGuardService.cs`: kiểm tra `.conf`, gọi duy nhất WireGuard chính thức; không đọc ra hay ghi log khóa riêng.
- `Services/AppLogger.cs`: nhật ký văn bản cục bộ tại `%LOCALAPPDATA%\NetBooster\operations.log`.
- `UI/MainForm.cs`: xác nhận rõ ràng trước mọi thay đổi và chỉ yêu cầu quyền Administrator lúc cần.

## Build và chạy

Yêu cầu Windows 10/11 và .NET 8 SDK (chỉ cần SDK để build).

```powershell
dotnet build .\NetBooster\NetBooster.csproj -c Release
dotnet run --project .\NetBooster\NetBooster.csproj
dotnet run --project .\NetBooster.Tests\NetBooster.Tests.csproj
dotnet publish .\NetBooster\NetBooster.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Tệp xuất bản nằm trong `NetBooster\bin\Release\net8.0-windows\win-x64\publish\NetBooster.exe`. Ứng dụng mở bình thường (không nâng quyền); khi đổi DNS hoặc bật/tắt WireGuard, ứng dụng hỏi xác nhận để mở lại bằng quyền Administrator.

## Cách dùng an toàn

1. Vào **Chẩn đoán**, bấm chạy chẩn đoán. Ping router và 1.1.1.1 được đo tám lần; kết quả có mất gói, trung vị và jitter.
2. Vào **DNS**, chọn card đang hoạt động, chạy benchmark. Mỗi DNS được đo với ba tên miền × ba lần, hiển thị trung vị và tỷ lệ lỗi. Chỉ đề xuất khi tốt hơn ít nhất 20%.
3. Chỉ bấm áp dụng sau khi xem kết quả. Trước khi đổi, DNS cũ được sao lưu; nếu lệnh lỗi, NetBooster tự khôi phục. Có nút khôi phục thủ công.
4. Với VPN, cài WireGuard từ [wireguard.com/install](https://www.wireguard.com/install/), chọn `.conf` của chính bạn, rồi xác nhận bật. Nút IP công khai chỉ gọi `api.ipify.org` sau khi bạn bấm.

## Kiểm thử thủ công cần thực hiện

| Tình huống | Kết quả mong đợi |
|---|---|
| Windows 10 và Windows 11 | Chẩn đoán, benchmark và UI hoạt động; DoH báo lỗi và rollback nếu cmdlet không có. |
| Đổi DNS rồi Khôi phục | DNS quay về đúng địa chỉ tĩnh/DHCP đã sao lưu. |
| Rút mạng khi đang đổi DNS | Báo lỗi và cố khôi phục cấu hình DNS cũ. |
| Không có quyền admin | Không thay đổi gì; có lời mời mở lại với quyền admin. |
| Chưa cài WireGuard | Báo rõ thiếu WireGuard, không tải hoặc cài hộ. |
| `.conf` sai | Từ chối trước khi gọi WireGuard; không hiển thị/private key. |
| Nhiều card/VPN khác | Chỉ thao tác card đã chọn; xác minh lại DNS của card không chọn không đổi. |

`NetBooster.Tests` là bộ test không phụ thuộc NuGet cho phần tính ping/jitter, kết luận và parser `netsh` với dữ liệu mẫu. Lệnh `dotnet run` ở trên phải in `All NetBooster logic tests passed.`

## Giới hạn trung thực

- Không đo được tốc độ download/upload thực và không "tăng tốc Internet". Ping/DNS chỉ giúp khoanh vùng nguyên nhân.
- Kết luận Wi‑Fi/nhà mạng là gợi ý dựa trên một phiên đo, không thay thế kiểm tra modem hoặc hỗ trợ từ ISP.
- Kiểm tra DNS sau VPN chỉ xác nhận truy vấn kiểm tra thành công, **không phải** một kiểm toán rò rỉ DNS toàn diện.
- Bản mã này chưa được chạy quyền Administrator, chưa thử với router/Wi‑Fi/VPN thật, và không thể xác nhận hành vi DoH trên từng bản Windows trong môi trường hiện tại.
- WireGuard service xác định tên tunnel theo tên tệp `.conf`; hãy tránh các tên trùng nhau và dùng giao diện WireGuard chính thức nếu cần quản lý nâng cao.

## Cải thiện sau bản đầu (không có trong bản này)

- Test tự động xUnit thực sự và parser `netsh` có fixture ngôn ngữ khác nhau.
- Hiển thị danh sách DNS preset để áp dụng đúng DNS được benchmark tốt nhất, thay vì preset Cloudflare duy nhất.
- Kiểm tra trạng thái WireGuard/tunnel chi tiết qua công cụ chính thức, và kiểm tra DNS leak minh bạch hơn với lựa chọn dịch vụ do người dùng quyết định.
