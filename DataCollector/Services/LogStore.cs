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
                File.WriteAllText(CsvPath, "timestamp_ms,mean_x,var_x,min_x,max_x,mean_y,var_y,min_y,max_y,mean_z,var_z,min_z,max_z,mean_a,var_a,min_a,max_a,type,bssid,rssi\n");
                LogPathChanged?.Invoke(CsvPath);
            }
            catch
            {
                // ignore
            }

            _initialized = true;
        }
    }

    public static void Append(long tsMs,
        double meanX, double varX, double minX, double maxX,
        double meanY, double varY, double minY, double maxY,
        double meanZ, double varZ, double minZ, double maxZ,
        double meanA, double varA, double minA, double maxA,
        string type, string bssid, int rssi)
    {
        EnsureInitialized();
        var line = $"{tsMs},{meanX:F4},{varX:F4},{minX:F4},{maxX:F4},{meanY:F4},{varY:F4},{minY:F4},{maxY:F4},{meanZ:F4},{varZ:F4},{minZ:F4},{maxZ:F4},{meanA:F4},{varA:F4},{minA:F4},{maxA:F4},{type},{bssid},{rssi}\n";
        try
        {
            lock (Sync)
            {
                File.AppendAllText(CsvPath, line);
                var count = Interlocked.Increment(ref _logCount);
                LogCountChanged?.Invoke(count);
            }
        }
        catch
        {
            // ignore
        }
    }

    public static string? RotateLog()
    {
        EnsureInitialized();

        lock (Sync)
        {
            if (_logCount <= 0 || string.IsNullOrWhiteSpace(CsvPath) || !File.Exists(CsvPath))
            {
                return null;
            }

            var oldPath = CsvPath;
            var folder = FileSystem.AppDataDirectory;
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var newPath = Path.Combine(folder, $"forklift_{ts}.csv");

            try
            {
                File.WriteAllText(newPath, "timestamp_ms,mean_x,var_x,min_x,max_x,mean_y,var_y,min_y,max_y,mean_z,var_z,min_z,max_z,mean_a,var_a,min_a,max_a,type,bssid,rssi\n");
                CsvPath = newPath;
                LogPathChanged?.Invoke(CsvPath);
                Interlocked.Exchange(ref _logCount, 0);
                LogCountChanged?.Invoke(0);
                return oldPath;
            }
            catch
            {
                return null;
            }
        }
    }
}
