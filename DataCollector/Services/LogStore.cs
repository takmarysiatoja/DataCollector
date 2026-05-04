namespace DataCollector;

public static class LogStore
{
    private static readonly object Sync = new();
    private static bool _initialized;
    private static int _logCount;

    public static string CsvPath { get; private set; } = "";
    public static int LogCount => _logCount;

    public static event Action<int> LogCountChanged;
    public static event Action<string> LogPathChanged;

    public static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (Sync)
        {
            if (_initialized) return;

            var folder = FileSystem.AppDataDirectory;
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            CsvPath = Path.Combine(folder, $"forklift_{ts}.csv");
            try
            {
                File.WriteAllText(CsvPath, "timestamp_ms,rms_x,rms_y,rms_z,raw_x,raw_y,raw_z,type,bssid,rssi\n");
                LogPathChanged?.Invoke(CsvPath);
            }
            catch
            {
                // ignore
            }

            _initialized = true;
        }
    }

    public static void Append(long tsMs, double rmsX, double rmsY, double rmsZ,
        double rawX, double rawY, double rawZ, string type, string bssid, int rssi)
    {
        EnsureInitialized();
        var line = $"{tsMs},{rmsX:F4},{rmsY:F4},{rmsZ:F4},{rawX:F4},{rawY:F4},{rawZ:F4},{type},{bssid},{rssi}\n";
        try
        {
            File.AppendAllText(CsvPath, line);
            var count = Interlocked.Increment(ref _logCount);
            LogCountChanged?.Invoke(count);
        }
        catch
        {
            // ignore
        }
    }
}
