using OhMyLockScreen.Shared.Models.SystemStatus;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace OhMyLockScreen.Services.SystemStatus;

internal sealed class BluetoothStatusObserver : IDisposable
{
    private readonly object _sync = new();
    private readonly Action<SystemStatusSnapshot> _publish;
    private readonly Action<Exception> _failure;
    private readonly Dictionary<string, DeviceInformation> _devices = [];
    private readonly List<DeviceWatcher> _watchers = [];
    private Radio? _radio;
    private SystemStatusAvailability _availability;
    private bool _disposed;
    private bool _failed;

    public BluetoothStatusObserver(Action<SystemStatusSnapshot> publish, Action<Exception> failure)
    {
        _publish = publish;
        _failure = failure;
        try
        {
            AddWatcher(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected));
            AddWatcher(BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected));
            _ = InitializeRadioAsync();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void AddWatcher(string selector)
    {
        var watcher = DeviceInformation.CreateWatcher(selector,
            ["System.Devices.Aep.IsConnected", "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.ContainerId", "System.Devices.ContainerId"],
            DeviceInformationKind.AssociationEndpoint);
        watcher.Added += OnAdded;
        watcher.Updated += OnUpdated;
        watcher.Removed += OnRemoved;
        watcher.EnumerationCompleted += OnEnumerationCompleted;
        watcher.Stopped += OnStopped;
        _watchers.Add(watcher);
        watcher.Start();
    }

    private async Task InitializeRadioAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            var radio = adapter == null ? null : await adapter.GetRadioAsync();
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _radio = radio;
                _availability = _failed ? SystemStatusAvailability.Unavailable
                    : adapter == null ? SystemStatusAvailability.Missing
                    : radio == null ? SystemStatusAvailability.Unavailable : SystemStatusAvailability.Available;
                if (radio != null)
                {
                    radio.StateChanged += OnRadioStateChanged;
                }

                Publish();
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation device)
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _devices[device.Id] = device;
                Publish();
            }
        }
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_sync)
        {
            if (!_disposed && _devices.TryGetValue(update.Id, out var device))
            {
                device.Update(update);
                Publish();
            }
        }
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _devices.Remove(update.Id);
                Publish();
            }
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        lock (_sync)
        {
            Publish();
        }
    }

    private void OnStopped(DeviceWatcher sender, object args)
    {
        if (sender.Status == DeviceWatcherStatus.Aborted)
        {
            Fail(new InvalidOperationException("蓝牙设备监听已中止。"));
        }
    }

    private void OnRadioStateChanged(Radio sender, object args)
    {
        lock (_sync)
        {
            Publish();
        }
    }

    // Caller owns _sync. No UI objects cross this boundary.
    private void Publish()
    {
        if (_disposed)
        {
            return;
        }

        var availability = _availability;
        if (availability == SystemStatusAvailability.Available && _radio?.State != RadioState.On)
        {
            availability = SystemStatusAvailability.Disabled;
        }

        var count = availability == SystemStatusAvailability.Available
            ? BluetoothDeviceIdentity.CountConnected(_devices.Values.Select(device => new BluetoothDeviceIdentity(
                device.Id, Property(device, "System.Devices.Aep.ContainerId") ?? Property(device, "System.Devices.ContainerId"),
                Property(device, "System.Devices.Aep.DeviceAddress"),
                device.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connected) && connected is true)))
            : 0;
        _publish(new SystemStatusSnapshot { Availability = availability, ConnectedDeviceCount = count });
    }

    private static string? Property(DeviceInformation device, string key) =>
        device.Properties.TryGetValue(key, out var value) ? value?.ToString() : null;

    private void Fail(Exception exception)
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _availability = SystemStatusAvailability.Unavailable;
                _failed = true;
                _failure(exception);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_radio != null)
            {
                _radio.StateChanged -= OnRadioStateChanged;
                _radio = null;
            }

            foreach (var watcher in _watchers)
            {
                watcher.Added -= OnAdded;
                watcher.Updated -= OnUpdated;
                watcher.Removed -= OnRemoved;
                watcher.EnumerationCompleted -= OnEnumerationCompleted;
                watcher.Stopped -= OnStopped;
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                {
                    watcher.Stop();
                }
            }

            _watchers.Clear();
            _devices.Clear();
        }
    }
}
