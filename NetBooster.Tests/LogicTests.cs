using System.Net.Sockets;
using NetBooster.Logic;
using NetBooster.Services;
using Xunit;

namespace NetBooster.Tests;

public class LogicTests
{
    // ==========================================
    // 1. Bufferbloat & Stability Logic Tests
    // ==========================================

    [Fact]
    public void Bufferbloat_Concludes_Stable_When_Latency_Does_Not_Increase_Significantly()
    {
        var baseline = new PingSummary("1.1.1.1", 10, 10, 0, 15, 2);
        var loaded = new PingSummary("1.1.1.1", 10, 10, 0, 18, 3);
        var comparison = BufferbloatLogic.Compare(baseline, loaded);

        var conclusion = BufferbloatLogic.Conclude(null, comparison, null);
        Assert.Equal("Mạng ổn định", conclusion);
    }

    [Fact]
    public void Bufferbloat_Concludes_High_Bufferbloat_When_Latency_Spikes_30ms_And_1_5x()
    {
        var baseline = new PingSummary("1.1.1.1", 10, 10, 0, 20, 2);
        var loaded = new PingSummary("1.1.1.1", 10, 10, 0, 65, 5); // +45ms and > 1.5x
        var comparison = BufferbloatLogic.Compare(baseline, loaded);

        var conclusion = BufferbloatLogic.Conclude(null, comparison, null);
        Assert.Contains("Bufferbloat cao", conclusion);
        Assert.Contains("SQM", conclusion);
    }

    [Fact]
    public void Bufferbloat_Concludes_High_Bufferbloat_When_Jitter_Spikes_20ms()
    {
        var baseline = new PingSummary("1.1.1.1", 10, 10, 0, 20, 2);
        var loaded = new PingSummary("1.1.1.1", 10, 10, 0, 35, 25); // +23ms jitter
        var comparison = BufferbloatLogic.Compare(baseline, loaded);

        var conclusion = BufferbloatLogic.Conclude(null, comparison, null);
        Assert.Contains("Bufferbloat cao", conclusion);
        Assert.Contains("Jitter", conclusion);
    }

    [Fact]
    public void Bufferbloat_Concludes_Unstable_Wifi_When_Gateway_Loses_Packets()
    {
        var gwBase = new PingSummary("192.168.1.1", 10, 10, 0, 2, 0.5);
        var gwLoaded = new PingSummary("192.168.1.1", 10, 8, 20, 10, 8); // 20% loss
        var inetBase = new PingSummary("1.1.1.1", 10, 10, 0, 20, 2);
        var inetLoaded = new PingSummary("1.1.1.1", 10, 10, 0, 25, 3);

        var gwComp = BufferbloatLogic.Compare(gwBase, gwLoaded);
        var inetComp = BufferbloatLogic.Compare(inetBase, inetLoaded);

        var conclusion = BufferbloatLogic.Conclude(gwComp, inetComp, null);
        Assert.Contains("Wi-Fi chập chờn, mất gói", conclusion);
    }

    [Fact]
    public void Bufferbloat_Handles_Load_Error_Gracefully()
    {
        var baseline = new PingSummary("1.1.1.1", 10, 10, 0, 15, 2);
        var loaded = new PingSummary("1.1.1.1", 10, 10, 0, 18, 3);
        var comparison = BufferbloatLogic.Compare(baseline, loaded);

        var conclusion = BufferbloatLogic.Conclude(null, comparison, "Download timed out");
        Assert.Contains("Không thể hoàn tất phép đo có tải", conclusion);
    }

    // ==========================================
    // 2. DNS Benchmark & Recommendation Logic Tests
    // ==========================================

    [Fact]
    public void DnsBenchmark_Recommends_Winner_When_Improvement_Is_Over_20_Percent_And_10ms()
    {
        var current = new DnsProvider("current", "DNS Hiện tại", new[] { "192.168.1.1" }, Array.Empty<string>());
        var currentMeasurements = new[]
        {
            new DnsMeasurement("192.168.1.1", AddressFamily.InterNetwork, CacheState.Cold, 45, 0, 6),
            new DnsMeasurement("192.168.1.1", AddressFamily.InterNetwork, CacheState.Hot, 40, 0, 6)
        };
        var currentBenchmark = new DnsProviderBenchmark(current, currentMeasurements);

        var cloudflare = new DnsProvider("cloudflare", "Cloudflare", new[] { "1.1.1.1" }, Array.Empty<string>());
        var cfMeasurements = new[]
        {
            new DnsMeasurement("1.1.1.1", AddressFamily.InterNetwork, CacheState.Cold, 15, 0, 6),
            new DnsMeasurement("1.1.1.1", AddressFamily.InterNetwork, CacheState.Hot, 10, 0, 6)
        };
        var cfBenchmark = new DnsProviderBenchmark(cloudflare, cfMeasurements);

        var decision = DnsBenchmarkLogic.Decide(currentBenchmark, new[] { cfBenchmark });

        Assert.True(decision.ShouldApply);
        Assert.NotNull(decision.Winner);
        Assert.Equal("cloudflare", decision.Winner!.Id);
    }

    [Fact]
    public void DnsBenchmark_Rejects_When_Improvement_Is_Under_10ms()
    {
        var current = new DnsProvider("current", "DNS Hiện tại", new[] { "192.168.1.1" }, Array.Empty<string>());
        var currentBenchmark = new DnsProviderBenchmark(current, new[]
        {
            new DnsMeasurement("192.168.1.1", AddressFamily.InterNetwork, CacheState.Cold, 22, 0, 6)
        });

        var candidate = new DnsProvider("cloudflare", "Cloudflare", new[] { "1.1.1.1" }, Array.Empty<string>());
        var candBenchmark = new DnsProviderBenchmark(candidate, new[]
        {
            new DnsMeasurement("1.1.1.1", AddressFamily.InterNetwork, CacheState.Cold, 16, 0, 6) // Improvement is 6ms (< 10ms)
        });

        var decision = DnsBenchmarkLogic.Decide(currentBenchmark, new[] { candBenchmark });

        Assert.False(decision.ShouldApply);
        Assert.Null(decision.Winner);
        Assert.Contains("chưa đạt ngưỡng", decision.Reason);
    }

    [Fact]
    public void DnsBenchmark_Rejects_When_Improvement_Is_Under_20_Percent()
    {
        var current = new DnsProvider("current", "DNS Hiện tại", new[] { "192.168.1.1" }, Array.Empty<string>());
        var currentBenchmark = new DnsProviderBenchmark(current, new[]
        {
            new DnsMeasurement("192.168.1.1", AddressFamily.InterNetwork, CacheState.Cold, 100, 0, 6)
        });

        var candidate = new DnsProvider("google", "Google", new[] { "8.8.8.8" }, Array.Empty<string>());
        var candBenchmark = new DnsProviderBenchmark(candidate, new[]
        {
            new DnsMeasurement("8.8.8.8", AddressFamily.InterNetwork, CacheState.Cold, 88, 0, 6) // Improvement is 12ms (12% < 20%)
        });

        var decision = DnsBenchmarkLogic.Decide(currentBenchmark, new[] { candBenchmark });

        Assert.False(decision.ShouldApply);
        Assert.Null(decision.Winner);
        Assert.Contains("chưa đạt ngưỡng", decision.Reason);
    }

    [Fact]
    public void DnsBenchmark_Rejects_When_Candidate_Has_Higher_Failure_Rate()
    {
        var current = new DnsProvider("current", "DNS Hiện tại", new[] { "192.168.1.1" }, Array.Empty<string>());
        var currentBenchmark = new DnsProviderBenchmark(current, new[]
        {
            new DnsMeasurement("192.168.1.1", AddressFamily.InterNetwork, CacheState.Cold, 50, 0, 6)
        });

        var candidate = new DnsProvider("fast-but-unreliable", "Fast", new[] { "1.2.3.4" }, Array.Empty<string>());
        var candBenchmark = new DnsProviderBenchmark(candidate, new[]
        {
            new DnsMeasurement("1.2.3.4", AddressFamily.InterNetwork, CacheState.Cold, 10, 20, 6) // 20% failure
        });

        var decision = DnsBenchmarkLogic.Decide(currentBenchmark, new[] { candBenchmark });

        Assert.False(decision.ShouldApply);
        Assert.Contains("tỷ lệ lỗi cao hơn", decision.Reason);
    }

    // ==========================================
    // 3. Wi-Fi Channel Recommendation Tests
    // ==========================================

    [Fact]
    public void WifiChannel_Recommends_Least_Crowded_24GHz_Channel_Among_1_6_11()
    {
        var networks = new List<WifiBss>
        {
            new("Net1", "00:11:22:33:44:01", 1, "2.4 GHz", 80),
            new("Net2", "00:11:22:33:44:02", 1, "2.4 GHz", 70),
            new("Net3", "00:11:22:33:44:03", 6, "2.4 GHz", 60),
            new("Net4", "00:11:22:33:44:04", 6, "2.4 GHz", 50),
            new("Net5", "00:11:22:33:44:05", 6, "2.4 GHz", 90)
            // Channel 11 has 0 networks
        };

        var recs = WifiChannelLogic.Recommend(networks);
        var rec24 = recs.FirstOrDefault(r => r.Band == "2.4 GHz");

        Assert.NotNull(rec24);
        Assert.Equal(11, rec24!.Channel); // Cleanest channel
        Assert.Equal(0, rec24.NearbyNetworks);
    }

    // ==========================================
    // 4. Ping Measurement Calculations Tests
    // ==========================================

    [Fact]
    public void MeasurementLogic_Calculates_Median_Loss_And_Jitter_Correctly()
    {
        var times = new long[] { 10, 15, 20, 25, 30 };
        var summary = MeasurementLogic.BuildPingSummary("192.168.1.1", 6, times); // 1 lost out of 6

        Assert.Equal(5, summary.Received);
        Assert.Equal(6, summary.Sent);
        Assert.InRange(summary.LossPercent, 16.6, 16.7);
        Assert.Equal(20, summary.MedianMs); // Middle element
        Assert.Equal(5, summary.JitterMs);  // Consecutive differences are all 5
    }

    [Fact]
    public void MeasurementLogic_Handles_Complete_Packet_Loss()
    {
        var summary = MeasurementLogic.BuildPingSummary("10.0.0.1", 5, Array.Empty<long>());

        Assert.Equal(0, summary.Received);
        Assert.Equal(5, summary.Sent);
        Assert.Equal(100, summary.LossPercent);
        Assert.Equal(-1, summary.MedianMs);
        Assert.Equal(0, summary.JitterMs);
        Assert.NotNull(summary.Error);
    }

    // ==========================================
    // 5. Power Plan Parser Tests
    // ==========================================

    [Fact]
    public void PowerPlanLogic_Parses_Active_Scheme_Guid_Correctly()
    {
        const string output = "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)";
        var success = PowerPlanLogic.TryParseActiveScheme(output, out var guid);

        Assert.True(success);
        Assert.Equal(Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), guid);
    }

    [Fact]
    public void PowerPlanLogic_Returns_False_On_Invalid_Output()
    {
        const string output = "No active scheme found or error occurred";
        var success = PowerPlanLogic.TryParseActiveScheme(output, out var guid);

        Assert.False(success);
        Assert.Equal(Guid.Empty, guid);
    }

    // ==========================================
    // 6. Native Wi-Fi PHY & Frequency Converter Tests
    // ==========================================

    [Theory]
    [InlineData(7, "802.11n (HT)")]
    [InlineData(8, "802.11ac (VHT)")]
    [InlineData(10, "802.11ax (HE)")]
    [InlineData(11, "802.11be (EHT)")]
    public void NativeWifi_PhyName_Maps_Correctly(uint phy, string expected)
    {
        var result = NativeWifi.PhyName(phy);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(2412000, 1, "2.4 GHz")]
    [InlineData(2437000, 6, "2.4 GHz")]
    [InlineData(2462000, 11, "2.4 GHz")]
    [InlineData(5180000, 36, "5 GHz")]
    [InlineData(5745000, 149, "5 GHz")]
    public void NativeWifi_ChannelFromFrequency_Maps_Correctly(uint freqKHz, int expectedChannel, string expectedBand)
    {
        var (channel, band) = NativeWifi.ChannelFromFrequencyKHz(freqKHz);
        Assert.Equal(expectedChannel, channel);
        Assert.Equal(expectedBand, band);
    }

    // ==========================================
    // 7. App Paths Tests
    // ==========================================

    [Fact]
    public void AppPaths_Uses_ProgramData_For_Backups()
    {
        var backupDir = AppPaths.BackupDirectory;
        Assert.Contains("ProgramData", backupDir, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("backups", backupDir, StringComparison.OrdinalIgnoreCase);
    }
}

