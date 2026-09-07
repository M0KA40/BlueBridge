using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using BlueBridge.Models;
using BlueBridge.Services;
using Forms = System.Windows.Forms;

namespace BlueBridge;

public partial class MainWindow : Window
{
    private readonly BluetoothReceiverService _receiver = new();
    private readonly AudioRoutingService _routing = new();
    private readonly SettingsService _settingsService = new();
    private readonly AppSettings _settings;
    private Forms.NotifyIcon? _tray;
    private bool _loading = true;
    private bool _busy;
    private bool _reallyClose;
    private bool _shutdownInProgress;

    public MainWindow(bool startup = false)
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        AutoConnectCheck.IsChecked = _settings.AutoConnectOnLaunch;
        StartupCheck.IsChecked = _settings.StartWithWindows;
        TrayCheck.IsChecked = _settings.CloseToTray;
        _receiver.DevicesChanged += (_, _) => Dispatcher.BeginInvoke(RefreshPhones);
        _receiver.StatusChanged += (_, status) => Dispatcher.BeginInvoke(() => ShowStatus(status));
        Loaded += async (_, _) =>
        {
            // Show the window first. Optional integrations must never prevent startup.
            _tray = TryCreateTray();
            await RefreshOutputsAsync();
            _receiver.StartWatching();
            _loading = false;
            if (startup) HideToTray();
            if (_settings.AutoConnectOnLaunch)
            {
                await Task.Delay(1400);
                RefreshPhones();
                if (PhoneCombo.SelectedItem is PhoneDevice) await ConnectSelectedAsync(false);
            }
        };
    }

    private Forms.NotifyIcon? TryCreateTray()
    {
        try
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open BlueBridge", null, (_, _) => ShowFromTray());
            menu.Items.Add("Repair audio once", null, async (_, _) => await RepairSelectedAsync());
            menu.Items.Add("Disconnect", null, async (_, _) => await _receiver.DisconnectAsync());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => { _reallyClose = true; Close(); });
            var tray = new Forms.NotifyIcon { Text = "BlueBridge", ContextMenuStrip = menu };
            string? executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
                tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(executable);
            tray.Visible = tray.Icon is not null;
            tray.DoubleClick += (_, _) => ShowFromTray();
            return tray;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Tray initialization failed; continuing without tray.", ex);
            return null;
        }
    }

    private void RefreshPhones()
    {
        string? selected = (PhoneCombo.SelectedItem as PhoneDevice)?.Id ?? _settings.LastDeviceId;
        IReadOnlyList<PhoneDevice> phones = _receiver.Devices;
        PhoneCombo.ItemsSource = phones;
        PhoneCombo.SelectedItem = phones.FirstOrDefault(x => x.Id == selected) ?? phones.FirstOrDefault();
    }

    private async Task RefreshOutputsAsync()
    {
        string selected = (OutputCombo.SelectedItem as AudioOutputDevice)?.Id ?? _settings.OutputDeviceId;
        IReadOnlyList<AudioOutputDevice> outputs = await Task.Run(_routing.GetOutputDevices);
        OutputCombo.ItemsSource = outputs;
        OutputCombo.SelectedItem = outputs.FirstOrDefault(x => x.Id == selected)
            ?? outputs.FirstOrDefault(x => x.Name == _settings.OutputDeviceName)
            ?? outputs[0];
    }

    private async Task ConnectSelectedAsync(bool repair)
    {
        if (_busy || PhoneCombo.SelectedItem is not PhoneDevice phone || OutputCombo.SelectedItem is not AudioOutputDevice output) return;
        _busy = true;
        ToggleControls(false);
        AudioRoutingService.DefaultSnapshot? defaults = null;
        try
        {
            // The A2DP capture port binds to the Windows default when it opens. Switch only for that instant.
            defaults = await _routing.SetTemporaryDefaultAsync(output);
            bool ok = repair ? await _receiver.RepairAudioAsync() : await _receiver.ConnectAsync(phone);
            if (ok)
            {
                await Task.Delay(500);
                await _routing.SetOutputForReceiverAsync(output, phone.DisplayName);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Audio binding failed.", ex);
            System.Windows.MessageBox.Show(this, ex.Message, "BlueBridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            try { await _routing.RestoreDefaultsAsync(defaults); }
            catch (Exception ex) { AppLogger.Error("Could not restore Windows defaults.", ex); System.Windows.MessageBox.Show(this, "Could not restore your original Windows default output. Please check Sound settings.", "BlueBridge", MessageBoxButton.OK, MessageBoxImage.Warning); }
            _busy = false;
            ToggleControls(true);
        }
    }

    private Task RepairSelectedAsync() => ConnectSelectedAsync(true);
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_receiver.IsConnected) await _receiver.DisconnectAsync();
        else await ConnectSelectedAsync(false);
    }
    private async void Repair_Click(object sender, RoutedEventArgs e) => await RepairSelectedAsync();
    private void RefreshPhones_Click(object sender, RoutedEventArgs e) => RefreshPhones();
    private async void RefreshOutputs_Click(object sender, RoutedEventArgs e) => await RefreshOutputsAsync();
    private void PhoneCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PhoneCombo.SelectedItem is PhoneDevice phone) { _settings.LastDeviceId = phone.Id; _settings.LastDeviceName = phone.Name; Save(); }
    }
    private async void OutputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputCombo.SelectedItem is not AudioOutputDevice output) return;
        _settings.OutputDeviceId = output.Id; _settings.OutputDeviceName = output.Name; Save();
        // Changing the route while connected is an explicit user action, so rebind exactly once.
        if (!_loading && _receiver.IsConnected) await ConnectSelectedAsync(true);
    }
    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.AutoConnectOnLaunch = AutoConnectCheck.IsChecked == true;
        _settings.CloseToTray = TrayCheck.IsChecked == true;
        Save();
    }
    private void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool enabled = StartupCheck.IsChecked == true;
        try { StartupService.SetEnabled(enabled); _settings.StartWithWindows = enabled; Save(); }
        catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "BlueBridge"); }
    }
    private void ShowStatus(ReceiverStatus status)
    {
        StatusTitle.Text = status.State switch { ReceiverState.Connected => "Connected", ReceiverState.Connecting => "Connecting", ReceiverState.Reconnecting => "Repairing", ReceiverState.Error => "Needs attention", _ => "Ready" };
        StatusDevice.Text = status.DeviceName ?? "Choose a Bluetooth audio device";
        StatusMessage.Text = status.Message;
        LiveText.Text = status.State == ReceiverState.Connected ? "● LIVE" : status.State == ReceiverState.Error ? "! CHECK" : "IDLE";
        LiveText.Foreground = status.State == ReceiverState.Connected ? System.Windows.Media.Brushes.SpringGreen : status.State == ReceiverState.Error ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.LightGray;
        ConnectButtonText.Text = status.State == ReceiverState.Connected ? "Disconnect" : "Connect device audio";
    }
    private void ToggleControls(bool enabled) { ConnectButton.IsEnabled = enabled; PhoneCombo.IsEnabled = enabled; OutputCombo.IsEnabled = enabled; }
    private void Save() { if (!_loading) _settingsService.Save(_settings); }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_reallyClose && _settings.CloseToTray) { e.Cancel = true; HideToTray(); return; }
        if (!_shutdownInProgress)
        {
            e.Cancel = true;
            _shutdownInProgress = true;
            ToggleControls(false);
            try { await _receiver.DisposeAsync(); }
            catch (Exception ex) { AppLogger.Error("Shutdown cleanup failed.", ex); }
            finally
            {
                _reallyClose = true;
                Close();
            }
            return;
        }
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        System.Windows.Application.Current.Shutdown();
    }
    private void HideToTray() { Hide(); ShowInTaskbar = false; }
    public void ShowFromTray()
    {
        ShowInTaskbar = true; Show(); WindowState = WindowState.Normal; Activate(); Topmost = true; Topmost = false;
    }
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else DragMove();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
