namespace BlueBridge;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\BlueBridge.Singleton";
    private const string ShowEventName = "Local\\BlueBridge.Show";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _showListenerCts;
    private MainWindow? _mainWindow;
    private bool _ownsMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        _ownsMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            try
            {
                EventWaitHandle.OpenExisting(ShowEventName).Set();
            }
            catch
            {
                // The first instance can still be starting; no duplicate is launched.
            }

            Shutdown();
            return;
        }

        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _showListenerCts = new CancellationTokenSource();
            _mainWindow = new MainWindow(e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase));
            MainWindow = _mainWindow;
            _mainWindow.Show();
            StartShowListener(_showListenerCts.Token);
        }
        catch (Exception ex)
        {
            Services.AppLogger.Error("BlueBridge could not start.", ex);
            System.Windows.MessageBox.Show(
                $"BlueBridge could not start.\n\n{ex.Message}\n\nLog: {Services.AppLogger.LogFilePath}",
                "BlueBridge",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void StartShowListener(CancellationToken cancellationToken)
    {
        _ = Task.Run(() =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_showEvent?.WaitOne(TimeSpan.FromSeconds(1)) == true)
                    {
                        Dispatcher.BeginInvoke(() => _mainWindow?.ShowFromTray());
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }, cancellationToken);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _showListenerCts?.Cancel();
        _showEvent?.Dispose();
        if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
