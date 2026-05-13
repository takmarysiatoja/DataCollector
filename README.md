# DataCollector

- Aplikacja .NET MAUI do zbierania danych z akcelerometru i skanów Wi‑Fi na urządzeniach Android.
- Zapis CSV przechowywany lokalnie w katalogu aplikacji, możliwość udostępnienia pliku z UI.

Wymagania
- .NET 9 / .NET MAUI
- Android target: Android 14 (TargetSdkVersion = 34)
- Architektura: `arm64-v8a` (w `DataCollector.csproj`: `AndroidSupportedAbis`)

Główne pliki
- UI: `MainPage.xaml`, `MainPage.xaml.cs`
- Background logging (foreground service): `Platforms/Android/LoggingForegroundService.cs`
- Log storage: `Services/LogStore.cs`
- Model Wi‑Fi: `Models/WifiEntry.cs`
- Manifest Android: `Platforms/Android/AndroidManifest.xml`

Uprawnienia (w `AndroidManifest.xml`)
- `ACCESS_WIFI_STATE`, `CHANGE_WIFI_STATE`
- `ACCESS_FINE_LOCATION`, `ACCESS_COARSE_LOCATION`
- `NEARBY_WIFI_DEVICES`
- `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_LOCATION`
- `POST_NOTIFICATIONS`

Aplikacja może działać w tle.

# UI:
<img width="591" height="945" alt="0" src="https://github.com/user-attachments/assets/bdc09c20-bb01-47b4-924a-119b69a1822f" />
