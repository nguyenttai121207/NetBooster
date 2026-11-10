using NetBooster;

namespace NetBooster.Tests;

// Lightweight dependency-free checks. Run as a console-style self test with `dotnet test` once a test runner is added,
// or keep these assertions as executable documentation for the pure calculation layer.
public static class MeasurementLogicTests
{
    public static void PingSummary_ReportsLossMedianAndJitter()
    {
        var result = Services.MeasurementLogic.PingSummary("router", 4, 3, new long[] { 10, 20, 30 });
        if (result.LossPercent != 25 || result.MedianMs != 20 || result.JitterMs != 10) throw new Exception("Calculation changed unexpectedly.");
    }
    public static void Conclusion_PrefersLocalWifiDiagnosis()
    {
        var gateway = new PingSummary("router", 8, 6, 25, 10, 25);
        var internet = new PingSummary("1.1.1.1", 8, 8, 0, 20, 1);
        if (!Services.MeasurementLogic.Conclusion(gateway, internet, 10, null).Contains("Wi‑Fi")) throw new Exception("Expected local diagnosis.");
    }
    public static void NetshParser_ReadsExpectedFields()
    {
        const string sample = "    SSID                   : HomeWiFi\r\n    Signal                 : 72%\r\n    Radio type             : 802.11ac\r\n    Channel                : 36\r\n    Receive rate (Mbps)    : 866.7\r\n    Transmit rate (Mbps)   : 866.7";
        var wifi = Services.NetworkDiagnostics.ParseNetshInterface(sample);
        if (wifi is null || wifi.Ssid != "HomeWiFi" || wifi.Channel != "36" || wifi.Signal != "72%") throw new Exception("netsh parser failed.");
    }
}
