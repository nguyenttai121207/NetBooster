# NetBooster 2.0 — Tối ưu hóa & Chẩn đoán Mạng Trung thực

**NetBooster 2.0** là ứng dụng Windows 10/11 một cửa sổ (Single Window) được xây dựng trên nền tảng **.NET 8 WinForms**, chuyên chẩn đoán chính xác nguyên nhân gây trễ/lag (Lag spikes, Bufferbloat, nhiễu kênh Wi-Fi, DNS chậm) khi chơi game, họp online và xem video.

> [!IMPORTANT]
> **Cam kết trung thực:** NetBooster **tuyệt đối KHÔNG tăng băng thông vượt gói cước** của nhà mạng và không sử dụng các kỹ thuật "tăng tốc giả". Ứng dụng chỉ áp dụng các điều chỉnh phía client đã được kiểm chứng trên Windows, không chứa Telemetry, không dịch vụ nền (Background Services), không tự khởi động (Startup) và không quảng cáo.

---

## 🌟 Điểm mới trong NetBooster 2.0

1. **Khởi chạy Administrator từ đầu**: Khai báo qua `app.manifest` (`requestedExecutionLevel="requireAdministrator"`), hỗ trợ hiển thị DPI cao PerMonitorV2 (125% - 200%).
2. **Loại bỏ triệt để**:
   - Xóa bỏ hoàn toàn Module WireGuard, nút kiểm tra IP công khai.
   - Xóa bỏ các thông số đo/hiển thị RAM, dung lượng ổ đĩa.
   - Xóa bỏ các "hồ sơ sử dụng" riêng lẻ để gom về luồng chẩn đoán tự động thông minh.
3. **Module Chẩn đoán & Bufferbloat**:
   - Đo Ping/Jitter/Packet loss đến Gateway và Internet (`1.1.1.1`).
   - Tạo tải ngắn với Cloudflare CDN để đo Bufferbloat và kết luận bằng tiếng Việt dễ hiểu: *"Mạng ổn định"*, *"Bufferbloat cao (Gợi ý bật SQM trên Router)"*, *"Wi-Fi chập chờn, mất gói"*.
4. **Tối ưu Card Wi-Fi Native & Driver**:
   - Sử dụng Native WiFi API (`wlanapi.dll`) thay vì parse chuỗi `netsh wlan`.
   - Tắt cờ tiết kiệm điện Adapter an toàn qua `Set-NetAdapterPowerManagement`.
   - Đổi Windows Power Plan sang High Performance (hỗ trợ duplicate scheme / Windows 11 Power Overlay nếu máy không có sẵn, tự động bỏ chọn khi đang chạy PIN).
   - Tối ưu thuộc tính Driver nâng cao (*Preferred Band 5GHz*, *Roaming Aggressiveness*) nếu phần cứng có hỗ trợ.
5. **Tiến trình ngốn mạng (Network Hogs)**:
   - Liệt kê các tiến trình đang chiếm dụng nhiều kết nối TCP nhất. Cho phép người dùng chọn đóng hoặc bỏ qua; không tự ý tắt tiến trình hệ thống.
6. **Delivery Optimization (Windows Update P2P)**:
   - Đọc và giới hạn băng thông nền của Windows Update ở mức 10% thông qua chính sách chính thức của Microsoft, hỗ trợ hoàn tác.
7. **Quét kênh Wi-Fi (Channel Scanner)**:
   - Quét danh sách SSID xung quanh, phân tích mật độ kênh và đưa ra khuyến nghị kênh thoáng (1, 6, 11 trên 2.4GHz hoặc các kênh 5GHz).
8. **DNS Benchmark & Optimization**:
   - Đo kiểm tra 7 tên miền phổ biến, phân biệt rõ cache lạnh và cache nóng, hỗ trợ IPv4 & IPv6.
   - Chỉ đề xuất đổi nếu DNS mới nhanh hơn tối thiểu **20% VÀ 10ms**.
9. **Sửa lỗi mạng nhanh (Network Repair)**:
   - Tích hợp trong mục Nâng cao: Release/Renew IP, Flush DNS Cache, Flush ARP Cache, Reset Winsock Catalog, Reset TCP/IP Stack.
10. **An toàn hệ thống & Rollback**:
    - Hiển thị bảng tóm tắt chi tiết các mục trước khi áp dụng.
    - Yêu cầu xác nhận chủ động từ người dùng.
    - Tự động sao lưu cấu hình gốc dưới dạng JSON tại `%ProgramData%\NetBooster\backups\`.
    - Tự động hoàn tác ngay lập tức nếu lệnh thi hành gặp lỗi.
    - Hỗ trợ tạo System Restore Point qua PowerShell 5.1 (`Checkpoint-Computer`), bắt cả exception và warning stream (giới hạn 24h, bảo vệ tắt) mà không làm crash ứng dụng.
    - Tự động xoay vòng file log (`operations.log`) khi đạt 5MB.

---

## 🏗️ Cấu trúc Kiến trúc

```
NetBooster/
├── app.manifest                 # Khởi chạy Admin, PerMonitorV2 High DPI, OS Compatibility
├── Models.cs                    # Toàn bộ Data Transfer Objects, Records & Enums
├── Program.cs                   # Entry point với PerMonitorV2 High DPI Mode
├── Logic/
│   ├── BufferbloatLogic.cs      # Tính toán delta trễ, jitter và xuất kết luận tiếng Việt
│   ├── DnsBenchmarkLogic.cs     # Thuật toán so sánh ngưỡng 20% & 10ms, kiểm tra tỷ lệ lỗi
│   ├── PowerPlanLogic.cs        # Regex parser cho GUID của Windows Power Schemes
│   └── WifiChannelLogic.cs      # Phân tích độ nghẽn kênh Wi-Fi và đề xuất kênh tối ưu
├── Services/
│   ├── AppLogger.cs             # Ghi log xoay vòng tại %LOCALAPPDATA%\NetBooster\ (5MB max)
│   ├── AppPaths.cs              # Quản lý đường dẫn sao lưu %ProgramData%\NetBooster\backups\
│   ├── BufferbloatService.cs    # Đo Ping rảnh vs Ping dưới tải (Cloudflare CDN 10MB)
│   ├── DeliveryOptimizationService.cs # Giới hạn băng thông nền Windows Update P2P
│   ├── DnsService.cs            # Benchmark DNS IPv4/IPv6, sao lưu và áp dụng/hoàn tác
│   ├── NativeWifi.cs            # P/Invoke wlanapi.dll với async scan notification & WlanFreeMemory
│   ├── NetworkDiagnostics.cs    # Đo Ping ICMP, gateway, độ trễ phân giải DNS
│   ├── NetworkRepairService.cs  # Renew IP, Flush DNS/ARP, Reset Winsock, Reset TCP/IP
│   ├── NetworkUsageService.cs   # Quét tiến trình mạng TCP, hỗ trợ kill process an toàn
│   ├── PowerPlanService.cs      # Quản lý Power Plan (High Perf, duplicate, Windows 11 overlay)
│   ├── ProcessRunner.cs         # Chạy tiến trình Windows & PowerShell an toàn (Base64 Encoded)
│   ├── ScanOrchestrator.cs      # Điều phối quét mạng đa luồng, cách ly lỗi từng module
│   ├── SystemRestoreService.cs  # Tạo điểm khôi phục qua PowerShell 5.1
│   └── WifiPowerService.cs      # Quản lý tiết kiệm điện và Driver properties qua NetAdapter
└── UI/
    └── MainForm.cs              # Giao diện đơn cửa sổ chuẩn Windows 11 (Light/Dark Mode)
```

---

## 🚀 Hướng dẫn Build và Chạy

### Yêu cầu
- Windows 10 (bản 1809 trở lên) hoặc Windows 11 (64-bit).
- .NET 8.0 SDK.

### 1. Build & Chạy ở chế độ Debug/Release
```powershell
# Restore và build
dotnet build .\NetBooster\NetBooster.csproj -c Release

# Chạy trực tiếp (sẽ hiện hộp thoại UAC yêu cầu Administrator)
dotnet run --project .\NetBooster\NetBooster.csproj
```

### 2. Chạy bộ Unit Tests (xUnit)
```powershell
dotnet test .\NetBooster.Tests\NetBooster.Tests.csproj
```

### 3. Xuất bản tệp thực thi độc lập (Self-Contained Single File)
```powershell
dotnet publish .\NetBooster\NetBooster.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
Tệp `.exe` độc lập sẽ được tạo tại:
`.\NetBooster\bin\Release\net8.0-windows\win-x64\publish\NetBooster.exe`
(Người dùng cuối có thể chạy trực tiếp mà không cần cài đặt trước .NET Runtime).

---

## 📋 Kịch bản Kiểm thử Thủ công (Manual Test Matrix)

| STT | Tình huống kiểm thử | Các bước thực hiện | Kết quả mong đợi |
|:---:|:---|:---|:---|
| **1** | Khởi chạy ứng dụng | Chạy `NetBooster.exe` từ tài khoản thường | Windows hiển thị UAC xin quyền Administrator ngay từ đầu; UI hiển thị nhãn "🛡️ Đang chạy với quyền Administrator". |
| **2** | Quét mạng tổng thể | Bấm nút lớn **"Quét Mạng Của Tôi"** | Thanh tiến trình chạy mượt từ 0% đến 100%, có nút Hủy; không đơ UI; 4 thẻ kết quả (Độ ổn định, Wi-Fi, DNS, Tiến trình) hiển thị icon Xanh/Vàng/Đỏ kèm giải thích tiếng Việt. |
| **3** | Hiển thị màn hình High DPI | Đặt tỉ lệ hiển thị Windows ở 125%, 150% hoặc 200% | Font chữ và các nút bấm sắc nét, không bị mờ hay vỡ layout (PerMonitorV2). |
| **4** | Tối ưu Wi-Fi Power Saving | Tích chọn "Tắt tiết kiệm điện card Wi-Fi" -> Bấm "Áp dụng mục đã chọn" | Cấu hình cũ được sao lưu vào `%ProgramData%\NetBooster\backups\wifi-power-backup.json`; các cờ Selective Suspend/DeviceSleep được tắt mà không sửa registry `PnPCapabilities`. |
| **5** | Đổi Power Plan trên Laptop | Chạy máy ở chế độ PIN -> Quét mạng | Mục High Performance không được tích sẵn mặc định; kèm chú thích cảnh báo nên cắm sạc. |
| **6** | Máy thiếu High Performance (Modern Standby S0) | Áp dụng High Performance trên máy chỉ có Balanced | Tự động thử `powercfg -duplicatescheme`; nếu không được thử Windows 11 Power Overlay; nếu phần cứng khóa hoàn toàn thì báo nhẹ "máy không hỗ trợ", không crash app. |
| **7** | Benchmark DNS & Đề xuất | Mở mục "Nâng cao" -> Bấm "Đo benchmark DNS" | Đo lần lượt 7 domain x Cold/Hot cache. Nếu DNS mới không nhanh hơn 20% VÀ 10ms -> nút Áp dụng bị vô hiệu hóa kèm giải thích rõ. |
| **8** | Tự động Rollback khi lỗi | Giả lập mất mạng hoặc ngắt card giữa chừng khi đổi DNS | Ứng dụng bắt lỗi, tự động gọi hàm Restore khôi phục lại DNS gốc đã sao lưu và ghi log chi tiết. |
| **9** | Nút Hoàn tác tất cả (Rollback) | Bấm nút "Hoàn tác tất cả" | Lần lượt khôi phục: Power Plan, Wi-Fi Power, Driver properties, DNS, Delivery Optimization về giá trị trước khi tối ưu. |
| **10** | Giới hạn 24h của System Restore | Bấm Áp dụng 2 lần liên tiếp trong ngày | Lần thứ 2 PowerShell báo hạn mức 24h -> ứng dụng hiện cảnh báo nhẹ trong log và tiếp tục áp dụng tối ưu, tuyệt đối không crash hay sửa registry tần suất. |
| **11** | File Log xoay vòng | Ghi log liên tục đạt ngưỡng 5MB | `operations.log` tự động đổi tên thành `operations.log.1` và tạo file mới; không vượt quá giới hạn đĩa. |

---

## ⚖️ Giới hạn Trung thực

- **Không vượt quá gói cước**: NetBooster không thể làm tăng tốc độ download/upload tối đa mà nhà mạng (ISP) đã ký trong hợp đồng.
- **Bufferbloat & SQM**: NetBooster có thể phát hiện hiện tượng Bufferbloat và giảm thiểu các lag spike ở phía máy tính, nhưng giải pháp triệt để nhất cho Bufferbloat vẫn là bật tính năng **SQM (Smart Queue Management)** hoặc QoS trực tiếp trên Router/Modem.
- **Sóng Wi-Fi & Vật cản**: Tối ưu hóa driver và tắt tiết kiệm điện giúp card Wi-Fi không bị nghẽn ngắt quãng, nhưng không thể thay thế cho khoảng cách vật lý quá xa hoặc tường chắn kim loại/bê tông dày.
- **DNS**: Đổi sang DNS nhanh hơn (như Cloudflare 1.1.1.1 hay Google 8.8.8.8) giúp tăng tốc độ mở trang lần đầu và giảm thời gian phân giải tên miền trong game, nhưng không làm tăng tốc độ truyền tải gói tin UDP gameplay thực tế.
