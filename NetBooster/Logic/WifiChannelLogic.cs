namespace NetBooster.Logic;

public static class WifiChannelLogic
{
    public static IReadOnlyList<ChannelRecommendation> Recommend(IEnumerable<WifiBss> networks)
    {
        var usable = networks.Where(x => x.Channel is not null && x.Band != "Không xác định").ToArray();
        var results = new List<ChannelRecommendation>();
        foreach (var band in usable.Select(x => x.Band).Distinct())
        {
            var counts = usable.Where(x => x.Band == band).GroupBy(x => x.Channel!.Value).ToDictionary(x => x.Key, x => x.Count());
            var candidates = band == "2.4 GHz" ? new[] { 1, 6, 11 } : counts.Keys.OrderBy(x => x).Take(24).ToArray();
            if (candidates.Length == 0) continue;
            var channel = candidates.OrderBy(c => counts.GetValueOrDefault(c)).ThenBy(c => c).First();
            results.Add(new ChannelRecommendation(band, channel, counts.GetValueOrDefault(channel), $"Gợi ý đổi trên router sang kênh {channel} ({band}) nếu router/điều kiện địa phương cho phép. Đây chỉ là gợi ý, ứng dụng không đổi router."));
        }
        return results;
    }
}
