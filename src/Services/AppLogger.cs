using System.Text;

namespace BlueBridge.Services;

public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BlueBridge");

    public static string LogFilePath => Path.Combine(DataFolder, "BlueBridge.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} | {exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DataFolder);
                RotateIfNeeded();
                string line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never interrupt audio playback.
        }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(LogFilePath);
        if (!file.Exists || file.Length < 2 * 1024 * 1024)
        {
            return;
        }

        string previous = Path.Combine(DataFolder, "BlueBridge.previous.log");
        File.Move(LogFilePath, previous, overwrite: true);
    }
}
