namespace NetBooster.Logic;

public static class DnsBenchmarkLogic
{
    public static DnsDecision Decide(DnsProviderBenchmark? current, IEnumerable<DnsProviderBenchmark> candidates)
    {
        if (current is null || current.MedianMs < 0) return new(false, "Không đo được DNS hiện tại một cách đáng tin cậy; không tự đề xuất đổi.", null);
        var best = candidates.Where(x => x.MedianMs >= 0).OrderBy(x => x.MedianMs).FirstOrDefault();
        if (best is null) return new(false, "Không có DNS thay thế nào trả lời đủ ổn định.", null);
        var improvement = current.MedianMs - best.MedianMs;
        var percentage = improvement / current.MedianMs * 100;
        if (best.FailurePercent > current.FailurePercent) return new(false, "DNS nhanh hơn có tỷ lệ lỗi cao hơn; không khuyến nghị đổi.", null);
        if (improvement < 10 || percentage < 20)
            return new(false, $"DNS hiện tại đã tốt: chênh lệch chỉ {Math.Max(0, improvement):F0} ms ({Math.Max(0, percentage):F0}%), chưa đạt ngưỡng 10 ms và 20%.", null);
        return new(true, $"{best.Provider.DisplayName} nhanh hơn {improvement:F0} ms ({percentage:F0}%) với tỷ lệ lỗi không cao hơn.", best.Provider);
    }
}
