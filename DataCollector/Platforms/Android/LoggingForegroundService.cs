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
    private const int RmsWindow = 10;
    private const int EmailIntervalMinutes = 1;

    private SensorManager _sensorManager;
    private Sensor _accelerometer;
    private readonly Queue<(double x, double y, double z)> _accBuf = new();
    private System.Timers.Timer _accelTimer;
    private System.Timers.Timer _wifiTimer;
    private System.Timers.Timer _countdownTimer;
    private System.Timers.Timer _emailTimer;
    private readonly Queue<string> _pendingEmailFiles = new();
    private readonly object _emailSync = new();
    private readonly SemaphoreSlim _emailSemaphore = new(1, 1);
    private EmailSender _emailSender;
    private double _lastRmsX, _lastRmsY, _lastRmsZ;
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
                _sensorManager.RegisterListener(this, _accelerometer, SensorDelay.Ui);
            }

            _accelTimer = new System.Timers.Timer(TimeSpan.FromSeconds(2));
            _accelTimer.Elapsed += (_, __) => SafeRun(OnAccelTick);
            _accelTimer.Start();

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
        _accelTimer?.Stop();
        _accelTimer?.Dispose();
        _accelTimer = null;

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
        lock (_accBuf)
        {
            _accBuf.Enqueue((e.Values[0], e.Values[1], e.Values[2]));
            while (_accBuf.Count > RmsWindow) _accBuf.Dequeue();
        }
    }

    private void OnAccelTick()
    {
        try
        {
            (double x, double y, double z)[] samples;
            lock (_accBuf) { samples = _accBuf.ToArray(); }
            if (samples.Length == 0) return;

            var rmsX = Math.Sqrt(samples.Average(s => s.x * s.x));
            var rmsY = Math.Sqrt(samples.Average(s => s.y * s.y));
            var rmsZ = Math.Sqrt(samples.Average(s => s.z * s.z));
            var last = samples[^1];
            _lastRmsX = rmsX;
            _lastRmsY = rmsY;
            _lastRmsZ = rmsZ;

            LogStore.Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                rmsX, rmsY, rmsZ, last.x, last.y, last.z, "ACCEL", "", 0);

            LoggingState.ReportAccel(new AccelSample(rmsX, rmsY, rmsZ, DateTime.Now));
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
                LogStore.Append(tsMs, _lastRmsX, _lastRmsY, _lastRmsZ, 0, 0, 0, "WIFI", en.Bssid, en.Rssi);
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
