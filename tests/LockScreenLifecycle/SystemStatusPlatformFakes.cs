// API stand-ins exercise the real Windows status adapters without touching hardware.
namespace Windows.System.Power
{
    public enum BatteryStatus { NotPresent, Discharging, Idle, Charging }
    public static class PowerManager
    {
        public static BatteryStatus BatteryStatus { get; set; } = BatteryStatus.NotPresent;
        public static int RemainingChargePercent { get; set; }
        public static event EventHandler<object>? BatteryStatusChanged;
        public static event EventHandler<object>? RemainingChargePercentChanged;
        public static event EventHandler<object>? PowerSupplyStatusChanged;
        public static int Subscribers => (BatteryStatusChanged?.GetInvocationList().Length ?? 0)
            + (RemainingChargePercentChanged?.GetInvocationList().Length ?? 0)
            + (PowerSupplyStatusChanged?.GetInvocationList().Length ?? 0);
        public static void Notify()
        {
            BatteryStatusChanged?.Invoke(null, new object());
            RemainingChargePercentChanged?.Invoke(null, new object());
            PowerSupplyStatusChanged?.Invoke(null, new object());
        }
    }
}

namespace Windows.Networking.Connectivity
{
    public enum NetworkConnectivityLevel { None, LocalAccess, ConstrainedInternetAccess, InternetAccess }
    public sealed class NetworkAdapter
    {
        public uint IanaInterfaceType { get; set; }
    }

    public sealed class WlanConnectionProfileDetails(string ssid)
    {
        public string GetConnectedSsid() => ssid;
    }

    public sealed class ConnectionProfile
    {
        public NetworkConnectivityLevel Level { get; set; }
        public string ProfileName { get; set; } = "";
        public bool IsWlanConnectionProfile { get; set; }
        public bool IsWwanConnectionProfile { get; set; }
        public NetworkAdapter? NetworkAdapter { get; set; }
        public WlanConnectionProfileDetails? WlanConnectionProfileDetails { get; set; }
        public NetworkConnectivityLevel GetNetworkConnectivityLevel() => Level;
    }

    public static class NetworkInformation
    {
        public static ConnectionProfile? Default { get; set; }
        public static ConnectionProfile[] Profiles { get; set; } = [];
        public static ConnectionProfile? GetInternetConnectionProfile() => Default;
        public static IReadOnlyList<ConnectionProfile> GetConnectionProfiles() => Profiles;
        public static event Action<object>? NetworkStatusChanged;
        public static int Subscribers => NetworkStatusChanged?.GetInvocationList().Length ?? 0;
        public static void Notify() => NetworkStatusChanged?.Invoke(new object());
    }
}

namespace Windows.Devices.Radios
{
    public enum RadioState { Unknown, On, Off, Disabled }
    public sealed class Radio
    {
        public RadioState State { get; set; } = RadioState.On;
        public event Action<Radio, object>? StateChanged;
        public int Subscribers => StateChanged?.GetInvocationList().Length ?? 0;
        public void Notify() => StateChanged?.Invoke(this, new object());
    }
}

namespace Windows.Devices.Bluetooth
{
    public enum BluetoothConnectionStatus { Disconnected, Connected }
    public static class BluetoothDevice
    {
        public static string GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus status) => "classic-connected";
    }

    public static class BluetoothLEDevice
    {
        public static string GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus status) => "ble-connected";
    }

    public sealed class BluetoothAdapter
    {
        public static Func<Task<BluetoothAdapter?>> Read { get; set; } = () => Task.FromResult<BluetoothAdapter?>(null);
        public Windows.Devices.Radios.Radio? Radio { get; set; }
        public static Task<BluetoothAdapter?> GetDefaultAsync() => Read();
        public Task<Windows.Devices.Radios.Radio?> GetRadioAsync() => Task.FromResult(Radio);
    }
}

namespace Windows.Devices.Enumeration
{
    public enum DeviceInformationKind { AssociationEndpoint }
    public enum DeviceWatcherStatus { Created, Started, EnumerationCompleted, Stopping, Stopped, Aborted }
    public sealed class DeviceInformation(string id)
    {
        public static List<DeviceWatcher> Watchers { get; } = [];
        public string Id { get; } = id;
        public Dictionary<string, object> Properties { get; } = [];
        public void Update(DeviceInformationUpdate update)
        {
            foreach (var property in update.Properties)
            {
                Properties[property.Key] = property.Value;
            }
        }

        public static DeviceWatcher CreateWatcher(string selector, string[] properties, DeviceInformationKind kind)
        {
            var watcher = new DeviceWatcher(selector);
            Watchers.Add(watcher);
            return watcher;
        }
    }

    public sealed class DeviceInformationUpdate(string id)
    {
        public string Id { get; } = id;
        public Dictionary<string, object> Properties { get; } = [];
    }

    public sealed class DeviceWatcher(string selector)
    {
        public string Selector { get; } = selector;
        public DeviceWatcherStatus Status { get; private set; }
        public event Action<DeviceWatcher, DeviceInformation>? Added;
        public event Action<DeviceWatcher, DeviceInformationUpdate>? Updated;
        public event Action<DeviceWatcher, DeviceInformationUpdate>? Removed;
        public event Action<DeviceWatcher, object>? EnumerationCompleted;
        public event Action<DeviceWatcher, object>? Stopped;
        public int Subscribers => (Added?.GetInvocationList().Length ?? 0)
            + (Updated?.GetInvocationList().Length ?? 0) + (Removed?.GetInvocationList().Length ?? 0)
            + (EnumerationCompleted?.GetInvocationList().Length ?? 0) + (Stopped?.GetInvocationList().Length ?? 0);
        public void Start() => Status = DeviceWatcherStatus.Started;
        public void Stop()
        {
            Status = DeviceWatcherStatus.Stopped;
            Stopped?.Invoke(this, new object());
        }

        public void Add(DeviceInformation device) => Added?.Invoke(this, device);
        public void Update(DeviceInformationUpdate update) => Updated?.Invoke(this, update);
        public void Remove(string id) => Removed?.Invoke(this, new DeviceInformationUpdate(id));
        public void Complete() => EnumerationCompleted?.Invoke(this, new object());
        public void Abort()
        {
            Status = DeviceWatcherStatus.Aborted;
            Stopped?.Invoke(this, new object());
        }
    }
}
