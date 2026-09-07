using Microsoft.Win32;

namespace BlueBridge.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BlueBridge";
    private const string LegacyValueName = "JackPhoneAudio";

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not determine the application path.");
            key.SetValue(ValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
    }
}
