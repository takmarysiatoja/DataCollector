#if ANDROID
using Android.App;
using Android.Content;
using Android.Hardware;
using Android.Net.Wifi;
using Android.OS;
using AndroidX.Core.App;
using System.Timers;

namespace DataCollector;

[Service(Exported = false, Name = "com.forklift.datacollector.LoggingForegroundService")]
public class LoggingForegroundService : Service, ISensorEventListener
{
    private const string ChannelId = "logging_channel";
    private const int NotificationId = 1001;
    public const string ActionStart = "DataCollector.action.START";
    public const string ActionStop = "DataCollector.action.STOP";
    private const int EmailIntervalMinutes = 10;
    private const int AccelBatchMaxReportLatencyUs = 2_000_000;
    private const long AccelBatchWindowNs = 2_000_000_000L;

    private SensorManager _sensorManager;
    private Sensor _accelerometer;
    private readonly List<(double x, double y, double z, long timestampNs)> _accBuf = new();
    private long? _accBatchStartNs;
    private System.Timers.Timer _wifiTimer;
    private System.Timers.Timer _countdownTimer;
    private System.Timers.Timer _emailTimer;
    private readonly Queue<string> _pendingEmailFiles = new();
    private readonly object _emailSync = new();
    private readonly SemaphoreSlim _emailSemaphore = new(1, 1);
    private EmailSender _emailSender;
    private double _lastMeanX, _lastVarX, _lastMinX, _lastMaxX;
    private double _lastMeanY, _lastVarY, _lastMinY, _lastMaxY;
    private double _lastMeanZ, _lastVarZ, _lastMinZ, _lastMaxZ;
    private double _lastMeanA, _lastVarA, _lastMinA, _lastMaxA;
    private int _wifiCountdown = 30;

    public static bool IsRunning { get; private set; }

    public static void Start(Context context)
    {
        var intent = new Intent(context, typeof(LoggingForegroundService));
        intent.SetAction(ActionStart);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    public static void Stop(Context context)
    {
        var intent = new Intent(context, typeof(LoggingForegroundService));
        intent.SetAction(ActionStop);
        context.StartService(intent);
    }

    public override void OnCreate()
    {
        base.OnCreate();
        LogStore.EnsureInitialized();
    }

    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        try
        {
            StartForegroundService();
            StartLogging();
            return StartCommandResult.Sticky;
        }
        catch (Exception ex)
        {
            LoggingState.ReportStatus($"Blad startu serwisu: {ex.Message}");
            StopSelf();
            return StartCommandResult.NotSticky;
        }
    }

    public override void OnDestroy()
    {
        StopLogging();
        IsRunning = false;
        LoggingState.ReportStatus("Zatrzymano logowanie w tle.");
        base.OnDestroy();
    }

    public override IBinder OnBind(Intent intent) => null;

    private void StartForegroundService()
    {
        var manager = (NotificationManager)GetSystemService(NotificationService);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(ChannelId, "Logowanie danych", NotificationImportance.Low)
            {
                Description = "Logowanie danych w tle"
            };
            manager.CreateNotificationChannel(channel);
        }

        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Logowanie danych aktywne")
            .SetContentText("Aplikacja zapisuje logi w tle")
            .SetSmallIcon(global::ForkliftDataCollector.Resource.Mipmap.appicon)
            .SetOngoing(true)
            .Build();

        StartForeground(NotificationId, notification);
        IsRunning = true;
        LoggingState.ReportStatus("Logowanie w tle aktywne.");
    }

    private void StartLogging()
    {
        try
        {
            _sensorManager = (SensorManager)GetSystemService(SensorService);
            _accelerometer = _sensorManager?.GetDefaultSensor(SensorType.Accelerometer);
            if (_accelerometer != null)
            {
                _sensorManager.RegisterListener(this, _accelerometer, SensorDelay.Game, AccelBatchMaxReportLatencyUs);
            }

            _wifiTimer = new System.Timers.Timer(TimeSpan.FromSeconds(30));
            _wifiTimer.Elapsed += (_, __) => SafeRun(DoWifiScan);
            _wifiTimer.Start();

            _countdownTimer = new System.Timers.Timer(TimeSpan.FromSeconds(1));
            _countdownTimer.Elapsed += (_, __) => SafeRun(UpdateCountdown);
            _countdownTimer.Start();

            _emailTimer = new System.Timers.Timer(TimeSpan.FromMinutes(EmailIntervalMinutes));
            _emailTimer.Elapsed += async (_, __) => await SafeRunAsync(SendEmailBatchAsync);
            _emailTimer.Start();

            UpdateCountdown();
        }
        catch (Exception ex)
        {
            LoggingState.ReportStatus($"Blad logowania: {ex.Message}");
            throw;
        }
    }

    private void SafeRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            LoggingState.ReportStatus($"Blad serwisu: {ex.Message}");
        }
    }

    private async Task SafeRunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            LoggingState.ReportStatus($"Blad serwisu: {ex.Message}");
        }
    }

    private void StopLogging()
    {
        _wifiTimer?.Stop();
        _wifiTimer?.Dispose();
        _wifiTimer = null;

        _countdownTimer?.Stop();
        _countdownTimer?.Dispose();
        _countdownTimer = null;

        _emailTimer?.Stop();
        _emailTimer?.Dispose();
        _emailTimer = null;

        if (_sensorManager != null)
        {
            _sensorManager.UnregisterListener(this);
        }
    }

    public void OnAccuracyChanged(Sensor sensor, SensorStatus accuracy)
    {
    }

    public void OnSensorChanged(SensorEvent e)
    {
        if (e?.Values == null || e.Values.Count < 3) return;

        (double x, double y, double z, long timestampNs)[] batch = null;
        lock (_accBuf)
        {
            _accBatchStartNs ??= e.Timestamp;
            _accBuf.Add((e.Values[0], e.Values[1], e.Values[2], e.Timestamp));
            if (e.Timestamp - _accBatchStartNs.Value >= AccelBatchWindowNs)
            {
                batch = _accBuf.ToArray();
                _accBuf.Clear();
                _accBatchStartNs = null;
            }
        }

        if (batch == null || batch.Length == 0) return;

        ProcessAccelBatch(batch);
    }

    private void ProcessAccelBatch((double x, double y, double z, long timestampNs)[] samples)
    {
        try
        {
            var count = samples.Length;
            if (count == 0) return;

            double sumX = 0;
            double sumY = 0;
            double sumZ = 0;
            double sumA = 0;
            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var minZ = double.PositiveInfinity;
            var minA = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;
            var maxZ = double.NegativeInfinity;
            var maxA = double.NegativeInfinity;
            var amplitudes = new double[count];

            for (var i = 0; i < count; i++)
            {
                var (x, y, z, _) = samples[i];
                var amplitude = Math.Sqrt((x * x) + (y * y) + (z * z));
                amplitudes[i] = amplitude;

                sumX += x;
                sumY += y;
                sumZ += z;
                sumA += amplitude;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                minZ = Math.Min(minZ, z);
                minA = Math.Min(minA, amplitude);

                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                maxZ = Math.Max(maxZ, z);
                maxA = Math.Max(maxA, amplitude);
            }

            var meanX = sumX / count;
            var meanY = sumY / count;
            var meanZ = sumZ / count;
            var meanA = sumA / count;
            double varX = 0;
            double varY = 0;
            double varZ = 0;
            double varA = 0;

            for (var i = 0; i < count; i++)
            {
                var xDelta = samples[i].x - meanX;
                var yDelta = samples[i].y - meanY;
                var zDelta = samples[i].z - meanZ;
                var aDelta = amplitudes[i] - meanA;

                varX += xDelta * xDelta;
                varY += yDelta * yDelta;
                varZ += zDelta * zDelta;
                varA += aDelta * aDelta;
            }

            varX /= count;
            varY /= count;
            varZ /= count;
            varA /= count;

            _lastMeanX = meanX;
            _lastVarX = varX;
            _lastMinX = minX;
            _lastMaxX = maxX;
            _lastMeanY = meanY;
            _lastVarY = varY;
            _lastMinY = minY;
            _lastMaxY = maxY;
            _lastMeanZ = meanZ;
            _lastVarZ = varZ;
            _lastMinZ = minZ;
            _lastMaxZ = maxZ;
            _lastMeanA = meanA;
            _lastVarA = varA;
            _lastMinA = minA;
            _lastMaxA = maxA;

            LogStore.Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                meanX, varX, minX, maxX,
                meanY, varY, minY, maxY,
                meanZ, varZ, minZ, maxZ,
                meanA, varA, minA, maxA,
                "ACCEL", "", 0);

            LoggingState.ReportAccel(new AccelSample(varX, varY, varZ, DateTime.Now));
        }
        catch (Exception ex)
        {
            LoggingState.ReportStatus($"Blad akcelerometru: {ex.Message}");
        }
    }

    private void UpdateCountdown()
    {
        try
        {
            _wifiCountdown--;
            if (_wifiCountdown <= 0) _wifiCountdown = 30;
            LoggingState.ReportWifiCountdown(_wifiCountdown);
        }
        catch (Exception ex)
        {
            LoggingState.ReportStatus($"Blad odliczania: {ex.Message}");
        }
    }

    private void DoWifiScan()
    {
        try
        {
            var wm = (WifiManager)GetSystemService(WifiService);
            if (wm == null) return;
#pragma warning disable CA1422
            wm.StartScan();
            var results = wm.ScanResults;
            if (results == null) return;
            var entries = results
                .OrderByDescending(r => r.Level)
                .Select(r => new WifiEntry { Bssid = $"{r.Bssid} [{r.Ssid}]", Rssi = r.Level })
                .ToList();
#pragma warning restore CA1422
            var tsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var en in entries)
            {
                LogStore.Append(tsMs,
                    _lastMeanX, _lastVarX, _lastMinX, _lastMaxX,
                    _lastMeanY, _lastVarY, _lastMinY, _lastMaxY,
                    _lastMeanZ, _lastVarZ, _lastMinZ, _lastMaxZ,
                    _lastMeanA, _lastVarA, _lastMinA, _lastMaxA,
                    "WIFI", en.Bssid, en.Rssi);
            }

            LoggingState.ReportWifi(entries);
            LoggingState.ReportStatus($"Skan Wi-Fi: {entries.Count} sieci | {DateTime.Now:HH:mm:ss}");
            _wifiCountdown = 30;
        }
        catch (Java.Lang.SecurityException)
        {
            LoggingState.ReportStatus("Brak uprawnien Wi-Fi/Lokalizacja");
        }
        catch (Exception ex)
        {
            LoggingState.ReportStatus($"Blad Wi-Fi: {ex.Message}");
        }
    }

    private async Task SendEmailBatchAsync()
    {
        if (!EmailSettings.IsConfigured)
        {
            LoggingState.ReportStatus("Brak konfiguracji SMTP. Uzupelnij EmailSettings.");
            return;
        }

        var rotated = LogStore.RotateLog();
        if (!string.IsNullOrWhiteSpace(rotated))
        {
            lock (_emailSync)
            {
                _pendingEmailFiles.Enqueue(rotated);
            }
        }

        await _emailSemaphore.WaitAsync();
        try
        {
            _emailSender ??= new EmailSender();
            while (true)
            {
                string next;
                lock (_emailSync)
                {
                    if (_pendingEmailFiles.Count == 0) break;
                    next = _pendingEmailFiles.Peek();
                }

                var success = await _emailSender.SendCsvAsync(next);
                if (!success)
                {
                    LoggingState.ReportStatus("Nie udalo sie wyslac CSV. Sprobujemy ponownie pozniej.");
                    break;
                }

                try
                {
                    File.Delete(next);
                }
                catch
                {
                    // ignore
                }

                lock (_emailSync)
                {
                    _pendingEmailFiles.Dequeue();
                }

                LoggingState.ReportStatus($"Wyslano log CSV: {Path.GetFileName(next)}");
            }
        }
        finally
        {
            _emailSemaphore.Release();
        }
    }
}
#endif
