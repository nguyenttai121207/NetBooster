namespace NetBooster.Tests;

internal static class Program
{
    static int Main()
    {
        try { MeasurementLogicTests.PingSummary_ReportsLossMedianAndJitter(); MeasurementLogicTests.Conclusion_PrefersLocalWifiDiagnosis(); MeasurementLogicTests.NetshParser_ReadsExpectedFields(); Console.WriteLine("All NetBooster logic tests passed."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
