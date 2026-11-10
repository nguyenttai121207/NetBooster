namespace NetBooster;

public record PingSummary(string Target, int Sent, int Received, double LossPercent, double MedianMs, double JitterMs, string? Error = null);
public record DnsMeasurement(string Server, double MedianMs, double FailurePercent, int Samples);
public record WifiInfo(string Ssid, string Signal, string RadioType, string Channel, string ReceiveRate, string TransmitRate);
public record DnsBackup(string AdapterName, bool IsDhcp, string[] Addresses, DateTime SavedAt);
