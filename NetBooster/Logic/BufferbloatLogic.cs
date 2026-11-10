namespace NetBooster.Logic;

public static class BufferbloatLogic
{
    public static BufferbloatEndpointResult Compare(PingSummary baseline, PingSummary loaded) => new(
        baseline,
        loaded,
        ValidDelta(baseline.MedianMs, loaded.MedianMs),
        ValidDelta(baseline.JitterMs, loaded.JitterMs));

    public static string Conclude(BufferbloatEndpointResult? gateway, BufferbloatEndpointResult internet, string? loadError)
    {
        if (!string.IsNullOrWhiteSpace(loadError)) return "Không thể hoàn tất phép tải tạo tải. Không kết luận về bufferbloat; hãy kiểm tra URL hoặc kết nối rồi đo lại.";
        if (gateway is not null && (gateway.Loaded.LossPercent > Math.Max(5, gateway.Baseline.LossPercent + 3) || gateway.Loaded.Received == 0))
            return "Có mất gói tới gateway khi có tải: ưu tiên kiểm tra Wi‑Fi/nội bộ (sóng, nhiễu, vị trí router) trước.";
        if (internet.Loaded.LossPercent > Math.Max(5, internet.Baseline.LossPercent + 3))
            return "Có mất gói ra Internet khi có tải. Nếu gateway vẫn ổn, đường truyền hoặc router có thể bị quá tải.";
        if (internet.MedianIncreaseMs >= 30 && internet.Loaded.MedianMs >= internet.Baseline.MedianMs * 1.5)
            return "Có dấu hiệu bufferbloat: độ trễ Internet tăng mạnh khi tải xuống. NetBooster không thể sửa ở PC; hãy bật/cấu hình SQM hoặc QoS trên router.";
        if (internet.JitterIncreaseMs >= 20)
            return "Jitter tăng rõ khi có tải. Hãy thử SQM/QoS trên router và đo lại, đồng thời kiểm tra thiết bị đang tải nền.";
        return "Trong lần đo này mạng khá ổn định khi có tải. Kết quả không chứng minh băng thông hoặc chất lượng sẽ luôn ổn định.";
    }

    private static double ValidDelta(double before, double after) => before < 0 || after < 0 ? double.NaN : after - before;
}
