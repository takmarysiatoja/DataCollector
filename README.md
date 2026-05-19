# DataCollector

- Aplikacja .NET MAUI do zbierania danych z akcelerometru i skanów Wi‑Fi na urządzeniach Android.
- Zapis CSV przechowywany lokalnie w katalogu aplikacji, możliwość udostępnienia pliku z UI.
- Dodatkowo: rotacja i wysyłka pliku CSV co 10 minut (jeśli skonfigurowano SMTP).

Wymagania
- .NET 9 / .NET MAUI
- Android target: Android 14 (TargetSdkVersion = 34)
- Architektura: `arm64-v8a` (w `DataCollector.csproj`: `AndroidSupportedAbis`)

Co jest zbierane
- Akcelerometr: hardware batching co ~2s (rejestracja z `SensorDelay.Game` i `maxReportLatencyUs = 2_000_000`).
  - Z każdego 2‑sekundowego okna obliczane są cechy statystyczne dla osi X, Y, Z oraz amplitudy (magnitude):
    - Mean (średnia)
    - Variance (wariancja) - obliczona jako sum((val-mean)^2)/N
    - Min (wartość minimalna)
    - Max (wartość maksymalna)
  - Razem 16 wartości numerycznych na okno: mean_x,var_x,min_x,max_x, mean_y,var_y,min_y,max_y, mean_z,var_z,min_z,max_z, mean_a,var_a,min_a,max_a
  - Do CSV zapisywany jest pojedynczy wiersz reprezentujący całe 2‑sekundowe okno z powyższymi cechami oraz znacznik czasu i typ `ACCEL`.
  - UI pokazuje aktualne wartości wariancji dla X/Y/Z w prostym formacie (X = var_x, Y = var_y, Z = var_z).
- Wi‑Fi: skan co 30s - każdy wynik skanu zapisywany jest do CSV razem z ostatnimi wyliczonymi cechami akcelerometru.


Główne pliki
- UI: `MainPage.xaml`, `MainPage.xaml.cs`
- Background logging (foreground service): `Platforms/Android/LoggingForegroundService.cs`
- Log storage: `Services/LogStore.cs`
- SMTP: `Services/EmailSettings.cs`, `S
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

