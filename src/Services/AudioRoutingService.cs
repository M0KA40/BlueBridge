using System.Diagnostics;
using System.Text.Json;
using BlueBridge.Models;

namespace BlueBridge.Services;

public sealed class AudioRoutingService
{
    private readonly string _svcl = Path.Combine(AppContext.BaseDirectory, "tools", "svcl.exe");
    private const string Columns = "Name,Type,Direction,Device Name,Command-Line Friendly ID,Item ID,Process ID,Process Path,Device State,Default,Default Multimedia,Default Communications";

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        var result = new List<AudioOutputDevice> { new(AudioOutputDevice.DefaultId, "Windows default") };
        try
        {
            foreach (SoundItem item in ReadItems())
            {
                if (!Eq(item.Type, "Device") || !Eq(item.Direction, "Render") ||
                    (!string.IsNullOrWhiteSpace(item.DeviceState) && !item.DeviceState.Contains("Active", StringComparison.OrdinalIgnoreCase))) continue;
                string id = RouteId(item);
                if (string.IsNullOrWhiteSpace(id)) continue;
                string name = string.IsNullOrWhiteSpace(item.DeviceName) ? item.Name : $"{item.Name} ({item.DeviceName})";
                if (result.All(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
                    result.Add(new AudioOutputDevice(id, name, IsYes(item.DefaultMultimedia)));
            }
        }
        catch (Exception ex) { AppLogger.Error("Could not enumerate outputs.", ex); }
        return result.Take(1).Concat(result.Skip(1)
            .OrderByDescending(x => x.Name.Contains("GoXLR", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => x.Name.Contains("PRO X 2", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)).ToArray();
    }

    public async Task<DefaultSnapshot?> SetTemporaryDefaultAsync(AudioOutputDevice output, CancellationToken token = default)
    {
        if (output.Id == AudioOutputDevice.DefaultId) return null;
        List<SoundItem> devices = (await Task.Run(ReadItems, token).ConfigureAwait(false))
            .Where(x => Eq(x.Type, "Device") && Eq(x.Direction, "Render")).ToList();
        string? console = RouteId(devices.FirstOrDefault(x => IsYes(x.Default)));
        string? multimedia = RouteId(devices.FirstOrDefault(x => IsYes(x.DefaultMultimedia)));
        string? communications = RouteId(devices.FirstOrDefault(x => IsYes(x.DefaultCommunications)));
        if (console is null || multimedia is null || communications is null)
            throw new InvalidOperationException("Could not safely save the current Windows default outputs.");
        var snapshot = new DefaultSnapshot(console, multimedia, communications);
        await RunAsync(["/SetDefault", output.Id, "all", "/Stdout"], token);
        AppLogger.Info($"Temporarily made '{output.Name}' the Windows default while opening A2DP.");
        return snapshot;
    }

    public async Task RestoreDefaultsAsync(DefaultSnapshot? snapshot, CancellationToken token = default)
    {
        if (snapshot is null) return;
        if (Eq(snapshot.ConsoleId, snapshot.MultimediaId) && Eq(snapshot.ConsoleId, snapshot.CommunicationsId))
            await RunAsync(["/SetDefault", snapshot.ConsoleId, "all", "/Stdout"], token);
        else
        {
            await RunAsync(["/SetDefault", snapshot.ConsoleId, "0", "/Stdout"], token);
            await RunAsync(["/SetDefault", snapshot.MultimediaId, "1", "/Stdout"], token);
            await RunAsync(["/SetDefault", snapshot.CommunicationsId, "2", "/Stdout"], token);
        }
        AppLogger.Info("Restored the user's original Windows default outputs.");
    }

    public async Task<bool> SetOutputForReceiverAsync(AudioOutputDevice output, string? deviceName, CancellationToken token = default)
    {
        List<SoundItem> items = await Task.Run(ReadItems, token).ConfigureAwait(false);
        List<SoundItem> sessions = items.Where(x => Eq(x.Type, "Application") &&
            (Contains(x.SearchText, deviceName) || Contains(x.SearchText, "A2DP SNK") || Contains(x.SearchText, "AudioPlaybackConnection"))).ToList();
        var pids = sessions.Select(x => x.ProcessId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        pids.Add(Environment.ProcessId.ToString());
        foreach (string pid in pids.Distinct())
            await RunAsync(["/SetAppDefault", output.Id, "all", pid, "/Stdout"], token);
        AppLogger.Info(sessions.Count > 0 ? $"Pinned A2DP receiver to '{output.Name}'." : "A2DP session not exposed yet; startup default binding remains active.");
        return sessions.Count > 0;
    }

    private List<SoundItem> ReadItems()
    {
        EnsureHelper();
        string path = Path.Combine(Path.GetTempPath(), $"BlueBridge-audio-{Guid.NewGuid():N}.json");
        try
        {
            Run(["/sjson", path, "/Columns", Columns]);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<SoundItem>();
            foreach (JsonElement row in doc.RootElement.EnumerateArray())
            {
                string Get(string key)
                {
                    foreach (JsonProperty p in row.EnumerateObject())
                        if (Normalize(p.Name) == Normalize(key)) return p.Value.ToString();
                    return string.Empty;
                }
                list.Add(new SoundItem(Get("Name"), Get("Type"), Get("Direction"), Get("Device Name"),
                    Get("Command-Line Friendly ID"), Get("Item ID"), Get("Process ID"), Get("Process Path"),
                    Get("Device State"), Get("Default"), Get("Default Multimedia"), Get("Default Communications")));
            }
            return list;
        }
        finally { try { File.Delete(path); } catch { } }
    }

    private void Run(IEnumerable<string> args) => RunAsync(args, CancellationToken.None).GetAwaiter().GetResult();
    private async Task RunAsync(IEnumerable<string> args, CancellationToken token)
    {
        EnsureHelper();
        using var process = new Process { StartInfo = new ProcessStartInfo(_svcl) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true } };
        foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
        if (!process.Start()) throw new InvalidOperationException("Could not start the audio routing helper.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        string error = await stderr.ConfigureAwait(false);
        _ = await stdout.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"Audio helper exited with code {process.ExitCode}." : error.Trim());
    }
    private void EnsureHelper() { if (!File.Exists(_svcl)) throw new FileNotFoundException("Bundled audio routing helper is missing.", _svcl); }
    private static string RouteId(SoundItem? x) => x is null ? string.Empty : !string.IsNullOrWhiteSpace(x.CommandLineId) ? x.CommandLineId : x.ItemId;
    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string text, string? value) => !string.IsNullOrWhiteSpace(value) && text.Contains(value, StringComparison.OrdinalIgnoreCase);
    private static bool IsYes(string? value) => value is not null && (value.Equals("Yes", StringComparison.OrdinalIgnoreCase) || value.Equals("True", StringComparison.OrdinalIgnoreCase) || value == "1");
    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public sealed record DefaultSnapshot(string ConsoleId, string MultimediaId, string CommunicationsId);
    private sealed record SoundItem(string Name, string Type, string Direction, string DeviceName, string CommandLineId,
        string ItemId, string ProcessId, string ProcessPath, string DeviceState, string Default,
        string DefaultMultimedia, string DefaultCommunications)
    {
        public string SearchText => $"{Name} {DeviceName} {ProcessPath} {ItemId}";
    }
}
