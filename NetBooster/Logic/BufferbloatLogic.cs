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
        if (!string.IsNullOrWhiteSpace(loadError)) 
            return "Không thể hoàn tất phép đo có tải (kiểm tra lại kết nối mạng hoặc URL).";
        
        // Loss on gateway indicates local Wi-Fi / LAN instability
        if (gateway is not null && (gateway.Loaded.LossPercent > 5 || gateway.Baseline.LossPercent > 5 || gateway.Loaded.JitterMs > 25))
            return "Wi-Fi chập chờn, mất gói (ưu tiên kiểm tra sóng Wi-Fi, vị trí router hoặc thử dùng dây LAN).";

        // Loss on internet while gateway is clean
        if (internet.Loaded.LossPercent > 5)
            return "Wi-Fi chập chờn, mất gói (mất gói ra Internet khi có tải; đường truyền hoặc modem bị quá tải).";

        // Latency spike >= 30ms and loaded latency >= 1.5x baseline
        if (internet.MedianIncreaseMs >= 30 && internet.Loaded.MedianMs >= internet.Baseline.MedianMs * 1.5)
            return "Bufferbloat cao (Gợi ý bật SQM trên Router để ổn định độ trễ khi có người tải mạng).";

        if (internet.JitterIncreaseMs >= 20)
            return "Bufferbloat cao (Gợi ý bật SQM trên Router - Jitter tăng mạnh khi có tải).";

        return "Mạng ổn định";
    }

    private static double ValidDelta(double before, double after) => before < 0 || after < 0 ? double.NaN : after - before;
}
