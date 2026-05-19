#if ANDROID
using Android.App;
#endif
using System.Collections.ObjectModel;

namespace DataCollector;

public partial class MainPage : ContentPage
{
    private readonly ObservableCollection<WifiEntry> _wifiEntries = new();
    private bool _running;

    public MainPage()
    {
        InitializeComponent();
        WifiList.ItemsSource = _wifiEntries;

        LogStore.EnsureInitialized();
        LblLogPath.Text = LogStore.CsvPath;
        LblLogCount.Text = $"Zapisanych wpisow: {LogStore.LogCount}";

        LogStore.LogCountChanged += OnLogCountChanged;
        LogStore.LogPathChanged += OnLogPathChanged;
        LoggingState.AccelUpdated += OnAccelUpdated;
        LoggingState.WifiUpdated += OnWifiUpdated;
        LoggingState.WifiCountdownChanged += OnWifiCountdownChanged;
        LoggingState.StatusChanged += OnStatusChanged;
    }

    private async void OnStartClicked(object s, EventArgs e)
    {
        if (_running) return;
        _running = true;
        BtnStart.IsEnabled = false;
        BtnStop.IsEnabled = true;
        StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#3FB950"));
        StatusLabel.Text = "Sprawdzam uprawnienia...";

        var (ok, message) = await EnsureAndroidPermissionsAsync();
        if (!ok)
        {
            _running = false;
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
            StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#484F58"));
            StatusLabel.Text = message;
            await DisplayAlert("Brak uprawnien", message, "OK");
            return;
        }

        StatusLabel.Text = "Pomiar aktywny...";
        StartForegroundLogging();
    }

    private async Task<(bool ok, string message)> EnsureAndroidPermissionsAsync()
    {
#if ANDROID
        var location = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (location != PermissionStatus.Granted)
        {
            return (false, "Wymagana zgoda na lokalizacje.");
        }
#if ANDROID33_0_OR_GREATER
        var wifi = await Permissions.RequestAsync<Permissions.NearbyWifiDevices>();
        if (wifi != PermissionStatus.Granted)
        {
            return (false, "Wymagana zgoda na Wi-Fi (Nearby devices).");
        }
        var notifications = await Permissions.RequestAsync<Permissions.PostNotifications>();
        if (notifications != PermissionStatus.Granted)
        {
            return (false, "Wymagana zgoda na powiadomienia (dla logowania w tle).");
        }
#endif
#endif
        return (true, "OK");
    }

    private void StopForegroundLogging()
    {
#if ANDROID
        LoggingForegroundService.Stop(Android.App.Application.Context);
#endif
    }

    private void OnAccelUpdated(AccelSample sample)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LblX.Text = sample.VarX.ToString("F3");
            LblY.Text = sample.VarY.ToString("F3");
            LblZ.Text = sample.VarZ.ToString("F3");
            LblAccelTime.Text = $"ostatni pomiar: {sample.Timestamp:HH:mm:ss}";
        });
    }

    private void OnWifiUpdated(IReadOnlyList<WifiEntry> entries)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _wifiEntries.Clear();
            foreach (var en in entries) _wifiEntries.Add(en);
        });
    }

    private void OnWifiCountdownChanged(int seconds)
    {
        MainThread.BeginInvokeOnMainThread(() =>
            LblWifiTime.Text = $"nastepny skan za: {seconds} s");
    }

    private void OnStatusChanged(string status)
    {
        MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = status);
    }

    private void OnLogCountChanged(int count)
    {
        MainThread.BeginInvokeOnMainThread(() => LblLogCount.Text = $"Zapisanych wpisow: {count}");
    }

    private void OnLogPathChanged(string path)
    {
        MainThread.BeginInvokeOnMainThread(() => LblLogPath.Text = path);
    }

    private void OnCopyPathClicked(object s, EventArgs e)
    {
        Clipboard.SetTextAsync(LogStore.CsvPath);
        StatusLabel.Text = "Sciezka skopiowana do schowka!";
    }

    private async void OnShareCsvClicked(object s, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(LogStore.CsvPath) || !File.Exists(LogStore.CsvPath))
            {
                StatusLabel.Text = "Brak pliku do udostepnienia.";
                return;
            }

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Udostepnij log CSV",
                File = new ShareFile(LogStore.CsvPath)
            });
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Blad udostepniania: {ex.Message}";
        }
    }

    private void StartForegroundLogging()
    {
#if ANDROID
        try
        {
            LoggingForegroundService.Start(Android.App.Application.Context);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Blad startu logowania: {ex.Message}";
        }
#endif
    }

    private void OnStopClicked(object s, EventArgs e)
    {
        _running = false;
        BtnStart.IsEnabled = true;
        BtnStop.IsEnabled = false;
        StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#484F58"));
        StatusLabel.Text = $"Zatrzymano. Zapisano {LogStore.LogCount} wpisow.";
        StopForegroundLogging();
        LblWifiTime.Text = "zatrzymany";
    }
}