using System.ComponentModel;

namespace DataCollector;

public class WifiEntry : INotifyPropertyChanged
{
    public string Bssid { get; set; } = "";
    public int Rssi { get; set; }
    public string RssiStr => $"{Rssi} dBm";
    public string RssiColor => Rssi >= -60 ? "#3FB950" : Rssi >= -75 ? "#D29922" : "#F85149";
    public event PropertyChangedEventHandler PropertyChanged;
}
