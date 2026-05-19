namespace DataCollector;

public readonly record struct AccelSample(double VarX, double VarY, double VarZ, DateTime Timestamp);

public static class LoggingState
{
    public static event Action<AccelSample> AccelUpdated;
    public static event Action<IReadOnlyList<WifiEntry>> WifiUpdated;
    public static event Action<int> WifiCountdownChanged;
    public static event Action<string> StatusChanged;

    public static void ReportAccel(AccelSample sample) => AccelUpdated?.Invoke(sample);

    public static void ReportWifi(IReadOnlyList<WifiEntry> entries) => WifiUpdated?.Invoke(entries);

    public static void ReportWifiCountdown(int seconds) => WifiCountdownChanged?.Invoke(seconds);

    public static void ReportStatus(string status) => StatusChanged?.Invoke(status);
}
