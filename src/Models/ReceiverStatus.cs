namespace BlueBridge.Models;

public enum ReceiverState
{
    Idle,
    Connecting,
    Connected,
    Reconnecting,
    Error,
    BluetoothUnavailable
}

public sealed record ReceiverStatus(ReceiverState State, string Message, string? DeviceName = null);
