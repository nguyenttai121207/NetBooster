namespace NetBooster.Services;

public sealed class UsageProfileService
{
    public static readonly IReadOnlyList<UsageProfile> Profiles = new[]
    {
        new UsageProfile("Chơi game", "Ưu tiên kiểm tra độ trễ và tránh tiết kiệm điện Wi‑Fi nếu card hỗ trợ.", new[]
        {
            "Đo bufferbloat: ping rảnh và ping khi tải xuống trong tối đa 7 giây.",
            "Đọc trạng thái Wi‑Fi và Power Management.",
            "Nếu có card Wi‑Fi hợp lệ: tắt các cờ tiết kiệm điện NetAdapter được Windows báo là hỗ trợ; lưu bản sao để hoàn tác.",
            "Không sửa registry TCP/Nagle/QoS, không thay đổi router và không hứa tăng băng thông."
        }, true),
        new UsageProfile("Họp online", "Ưu tiên ổn định Wi‑Fi và phát hiện cạnh tranh lưu lượng.", new[]
        {
            "Đo bufferbloat/packet loss trong tối đa 7 giây.",
            "Quét kênh Wi‑Fi xung quanh, chỉ đưa gợi ý cho router.",
            "Nếu có card Wi‑Fi hợp lệ: tắt các cờ tiết kiệm điện NetAdapter được hỗ trợ; lưu bản sao để hoàn tác.",
            "Không tự đóng ứng dụng hoặc thay đổi DNS/router."
        }, true),
        new UsageProfile("Xem video", "Ưu tiên tìm cạnh tranh lưu lượng nền thay vì hứa tăng tốc video.", new[]
        {
            "Liệt kê các tiến trình có kết nối TCP đang hoạt động (không tự đóng tiến trình nào).",
            "Đọc Delivery Optimization. Nếu Windows hỗ trợ: giới hạn tải nền của Delivery Optimization ở 10%; lưu bản sao để hoàn tác.",
            "Không đổi DNS, không tắt dịch vụ Windows, không thay đổi router."
        }, true)
    };

    public UsageProfile Get(string name) => Profiles.First(x => x.Name == name);
}
