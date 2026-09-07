namespace BlueBridge.Models;

public sealed record PhoneDevice(string Id, string Name)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Bluetooth audio device" : Name;
    public override string ToString() => DisplayName;
}
