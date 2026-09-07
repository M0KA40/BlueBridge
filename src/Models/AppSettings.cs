namespace BlueBridge.Models;

public sealed class AppSettings
{
    public string? LastDeviceId { get; set; }
    public string? LastDeviceName { get; set; }
    public string OutputDeviceId { get; set; } = AudioOutputDevice.DefaultId;
    public string OutputDeviceName { get; set; } = "Windows default";
    public bool AutoConnectOnLaunch { get; set; }
    public bool StartWithWindows { get; set; }
    public bool CloseToTray { get; set; } = true;
}
