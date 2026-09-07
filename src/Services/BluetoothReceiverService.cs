using BlueBridge.Models;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BlueBridge.Services;

public sealed class BluetoothReceiverService : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, PhoneDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private DeviceWatcher? _watcher;
    private AudioPlaybackConnection? _connection;
    private string? _targetId;
    private string? _targetName;
    private bool _wanted;
    private bool _disposed;
    private ReceiverStatus _status = new(ReceiverState.Idle, "Ready to connect");

    public event EventHandler? DevicesChanged;
    public event EventHandler<ReceiverStatus>? StatusChanged;
    public ReceiverStatus Status => _status;
    public bool IsConnected => _status.State == ReceiverState.Connected;
    public IReadOnlyList<PhoneDevice> Devices
    {
        get { lock (_sync) return _devices.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(); }
    }

    public void StartWatching()
    {
        if (_watcher is not null || _disposed) return;
        try
        {
            _watcher = DeviceInformation.CreateWatcher(AudioPlaybackConnection.GetDeviceSelector());
            _watcher.Added += Added;
            _watcher.Updated += Updated;
            _watcher.Removed += Removed;
            _watcher.EnumerationCompleted += Enumerated;
            _watcher.Start();
            AppLogger.Info("Bluetooth audio device discovery started.");
        }
        catch (Exception ex)
        {
            SetStatus(ReceiverState.BluetoothUnavailable, "Bluetooth receiver is unavailable");
            AppLogger.Error("Bluetooth discovery failed.", ex);
        }
    }

    public Task<bool> ConnectAsync(PhoneDevice device, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _targetId = device.Id;
        _targetName = device.DisplayName;
        _wanted = true;
        return OpenOnceAsync(false, token);
    }

    public async Task<bool> RepairAudioAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(_targetId))
        {
            SetStatus(ReceiverState.Error, "Choose a Bluetooth device first");
            return false;
        }
        _wanted = true;
        await _gate.WaitAsync(token);
        try
        {
            SetStatus(ReceiverState.Reconnecting, "Repairing audio once…", _targetName);
            DisposeConnection();
        }
        finally { _gate.Release(); }
        await Task.Delay(700, token);
        return await OpenOnceAsync(true, token);
    }

    public async Task DisconnectAsync()
    {
        _wanted = false;
        await _gate.WaitAsync();
        try
        {
            DisposeConnection();
            SetStatus(ReceiverState.Idle, "Disconnected", _targetName);
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> OpenOnceAsync(bool repair, CancellationToken token)
    {
        if (!_wanted || string.IsNullOrWhiteSpace(_targetId)) return false;
        await _gate.WaitAsync(token);
        try
        {
            DisposeConnection();
            SetStatus(repair ? ReceiverState.Reconnecting : ReceiverState.Connecting,
                repair ? "Opening the audio stream…" : "Connecting to your device…", _targetName);
            AudioPlaybackConnection? candidate = AudioPlaybackConnection.TryCreateFromId(_targetId);
            if (candidate is null) throw new InvalidOperationException("Windows could not create the Bluetooth audio port.");
            _connection = candidate;
            candidate.StateChanged += StateChanged;
            await candidate.StartAsync();
            AudioPlaybackConnectionOpenResult result = await candidate.OpenAsync();
            if (result.Status == AudioPlaybackConnectionOpenResultStatus.Success)
            {
                SetStatus(ReceiverState.Connected, "Device audio is playing on this PC", _targetName);
                return true;
            }
            string message = result.Status switch
            {
                AudioPlaybackConnectionOpenResultStatus.RequestTimedOut => "The device did not answer in time",
                AudioPlaybackConnectionOpenResultStatus.DeniedBySystem => "Windows denied the audio connection",
                _ => result.ExtendedError?.Message ?? "Windows could not open the audio stream"
            };
            DisposeConnection();
            SetStatus(ReceiverState.Error, message, _targetName);
            return false;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            DisposeConnection();
            SetStatus(ReceiverState.Error, string.IsNullOrWhiteSpace(ex.Message) ? "Could not connect" : ex.Message, _targetName);
            AppLogger.Error("Bluetooth connection failed.", ex);
            return false;
        }
        finally { _gate.Release(); }
    }

    private void StateChanged(AudioPlaybackConnection sender, object args)
    {
        if (sender.State == AudioPlaybackConnectionState.Closed && _wanted)
            SetStatus(ReceiverState.Error, "Audio stopped — press Repair audio", _targetName);
    }
    private void DisposeConnection()
    {
        AudioPlaybackConnection? old = _connection;
        _connection = null;
        if (old is null) return;
        try { old.StateChanged -= StateChanged; } catch { }
        try { old.Dispose(); } catch { }
    }
    private void Added(DeviceWatcher sender, DeviceInformation item)
    {
        lock (_sync) _devices[item.Id] = new PhoneDevice(item.Id, item.Name);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Updated(DeviceWatcher sender, DeviceInformationUpdate item) => DevicesChanged?.Invoke(this, EventArgs.Empty);
    private void Removed(DeviceWatcher sender, DeviceInformationUpdate item)
    {
        lock (_sync) _devices.Remove(item.Id);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        if (_wanted && string.Equals(item.Id, _targetId, StringComparison.OrdinalIgnoreCase))
            SetStatus(ReceiverState.Error, "Bluetooth device is no longer available", _targetName);
    }
    private void Enumerated(DeviceWatcher sender, object args) => DevicesChanged?.Invoke(this, EventArgs.Empty);
    private void SetStatus(ReceiverState state, string message, string? device = null)
    {
        _status = new ReceiverStatus(state, message, device);
        StatusChanged?.Invoke(this, _status);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _wanted = false;
        if (_watcher is not null)
        {
            try { _watcher.Stop(); } catch { }
            _watcher.Added -= Added; _watcher.Updated -= Updated; _watcher.Removed -= Removed; _watcher.EnumerationCompleted -= Enumerated;
        }
        await _gate.WaitAsync();
        try { DisposeConnection(); } finally { _gate.Release(); }
        _gate.Dispose();
    }
}
