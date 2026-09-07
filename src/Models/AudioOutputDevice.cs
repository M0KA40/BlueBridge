namespace BlueBridge.Models;

public sealed record AudioOutputDevice(string Id, string Name, bool IsWindowsDefault = false)
{
    public const string DefaultId = "DefaultRenderDevice";

    public string DisplayName => Id == DefaultId
        ? "Windows default"
        : IsWindowsDefault ? $"{Name}  •  current default" : Name;

    public override string ToString() => DisplayName;
}
