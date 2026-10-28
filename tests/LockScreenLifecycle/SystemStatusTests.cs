using FluentIcons.Common;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.SystemStatus;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.SystemStatus;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Models.SystemStatus;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

internal static class SystemStatusTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Production host resolves system status services and creates independent component instances", () =>
        {
            var factory = typeof(OhMyLockScreen.App).GetMethod("CreateHost", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            using var host = (Microsoft.Extensions.Hosting.IHost)factory.Invoke(null, null)!;
            var service = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ISystemStatusService>(host.Services);
            var components = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<OhMyLockScreen.Shared.Abstractions.Services.Scene.IComponentsService>(host.Services);
            var settings = new ComponentSettings { Id = SystemStatusComponent.Id };
            var first = components.CreateComponent(settings);
            var second = components.CreateComponent(settings);
            Check(first is SystemStatusComponent && second is SystemStatusComponent && !ReferenceEquals(first, second), "Production component registration failed.");
            Check(ReferenceEquals(service, Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ISystemStatusService>(host.Services)), "Service is not shared.");
        });

        run("System status defaults, independent feature instances and polymorphic settings survive persistence", () =>
        {
            var settings = new SystemStatusComponentSettings();
            Check(settings.Kind == SystemStatusKind.Battery && settings.ShowIcon && !settings.ShowText && settings.IconSize == 24, "Incorrect defaults.");
            var first = new SystemStatusDefaultFeaturesProvider().CreateDefaultFeatures().Single();
            var second = new SystemStatusDefaultFeaturesProvider().CreateDefaultFeatures().Single();
            Check(!ReferenceEquals(first.Settings, second.Settings), "Default features share mutable settings.");
            Check(first.Settings is OhMyLockScreen.Models.Features.Appearance.TextFeatureSettings { FontSize: 16 }, "Default text size is not 16 logical pixels.");
            Check(System.Text.Json.JsonSerializer.Deserialize<SystemStatusComponentSettings>("{}")!.IconSize == 24,
                "Older settings without IconSize lost the default icon size.");
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForCurrentAssembly();
            settings.Kind = SystemStatusKind.Bluetooth;
            settings.ShowIcon = false;
            settings.ShowText = true;
            settings.IconSize = 37.5;
            var root = new SceneSettings { Layers = [new LayerSettings { Children = [new ComponentSettings { Id = SystemStatusComponent.Id, Settings = settings }] }] };
            var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "system-status.json");
            ConfigureFileHelper.SaveConfig(path, root, true);
            var loaded = ConfigureFileHelper.LoadConfig<SceneSettings>(path)!;
            var restored = (SystemStatusComponentSettings)loaded.Layers[0].Children[0].Settings!;
            Check(restored.Kind == settings.Kind && !restored.ShowIcon && restored.ShowText && restored.IconSize == 37.5, "Settings failed to round-trip.");
            Check(File.ReadAllText(path).Contains("omls.systemStatus"), "Settings have no stable type discriminator.");
        });

        run("System status presentation preserves percentages and distinguishes missing, offline and disabled states", () =>
        {
            var available = new SystemStatusSnapshot { Availability = SystemStatusAvailability.Available, Percent = 85 };
            Check(Display(SystemStatusKind.Battery, available) == new SystemStatusPresentation(Icon.Battery9, "85%"), "Battery display incorrect.");
            Check(Display(SystemStatusKind.Battery, available with { IsCharging = true }).Icon == Icon.BatteryCharge9, "Charging icon missing.");
            Check(Display(SystemStatusKind.Battery, available with { Percent = 0 }).Icon == Icon.Battery0, "Empty battery icon missing.");
            Check(Display(SystemStatusKind.Battery, available with { Percent = 100 }).Icon == Icon.Battery10, "Full battery icon missing.");
            Check(Display(SystemStatusKind.Volume, available with { IsMuted = true }) == new SystemStatusPresentation(Icon.SpeakerMute, "85%"), "Mute erased the configured volume.");
            Check(Display(SystemStatusKind.Network, available with { NetworkName = "Home", NetworkKind = SystemNetworkKind.WiFi }).Text == "Home", "Network name missing.");
            Check(Display(SystemStatusKind.Network, available with { NetworkKind = SystemNetworkKind.Ethernet }).Icon == Icon.Connector, "Ethernet rendered as Wi-Fi.");
            Check(Display(SystemStatusKind.Network, available with { NetworkKind = SystemNetworkKind.WiFi }).Icon == Icon.WiFi, "Wi-Fi type icon missing.");
            Check(Display(SystemStatusKind.Network, available with { NetworkKind = SystemNetworkKind.Cellular }).Icon == Icon.CellularData5, "Mobile data type icon missing.");
            Check(Display(SystemStatusKind.Network, new() { Availability = SystemStatusAvailability.Disconnected }).Icon == Icon.GlobeOff, "No network was represented as disabled Wi-Fi.");
            Check(Display(SystemStatusKind.Bluetooth, available with { ConnectedDeviceCount = 3 }).Text == "已连接 3 台设备", "Bluetooth count missing.");
            Check(Display(SystemStatusKind.Bluetooth, available).Text == "未连接设备", "Zero devices displayed as connected.");
            Check(Display(SystemStatusKind.Battery, new() { Availability = SystemStatusAvailability.Missing }).Text == "无电池", "Missing battery fabricated a percentage.");
            Check(Display(SystemStatusKind.Network, new() { Availability = SystemStatusAvailability.Disconnected }).Text == "未连接网络", "Disconnected network hidden.");
            Check(Display(SystemStatusKind.Volume, new() { Availability = SystemStatusAvailability.Missing }).Text == "无音频输出设备", "Missing endpoint hidden.");
            Check(Display(SystemStatusKind.Bluetooth, new() { Availability = SystemStatusAvailability.Disabled }).Text == "蓝牙已关闭", "Disabled radio indistinguishable from disconnected.");
            foreach (var kind in Enum.GetValues<SystemStatusKind>())
            {
                Check(Display(kind, SystemStatusSnapshot.Unavailable).Text == "状态不可用", "A read failure fabricated state.");
            }
        });

        run("Bluetooth counts only connected physical devices and deduplicates containers or addresses", () =>
        {
            BluetoothDeviceIdentity[] devices =
            [
                new("classic", "container-a", "AA", true),
                new("ble", "CONTAINER-A", "BB", true),
                new("address-1", null, "CC", true),
                new("address-2", Guid.Empty.ToString(), "cc", true),
                new("offline", "container-b", "DD", false),
                new("fallback", null, null, true)
            ];
            Check(BluetoothDeviceIdentity.CountConnected(devices) == 3, "Duplicate or disconnected endpoints were counted.");
            Check(BluetoothDeviceIdentity.CountConnected(
            [
                new("address-only", null, "aa:bb", true),
                new("classic", "{11111111-1111-1111-1111-111111111111}", "AA-BB", true),
                new("ble", "11111111-1111-1111-1111-111111111111", "cc:dd", true),
                new("empty-container", "{00000000-0000-0000-0000-000000000000}", "CC-DD", true)
            ]) == 1, "Partial identities or formatted aliases inflated the count.");
        });

        run("Windows battery adapter reads initial state, charging changes and detaches all native events", () =>
        {
            var source = new WindowsSystemStatusSource(NullLogger<WindowsSystemStatusSource>.Instance);
            var values = new List<SystemStatusSnapshot>();
            Windows.System.Power.PowerManager.BatteryStatus = Windows.System.Power.BatteryStatus.NotPresent;
            var watch = source.Watch(SystemStatusKind.Battery, values.Add);
            Check(values[^1].Availability == SystemStatusAvailability.Missing, "No battery was reported as charged.");
            Windows.System.Power.PowerManager.BatteryStatus = Windows.System.Power.BatteryStatus.Charging;
            Windows.System.Power.PowerManager.RemainingChargePercent = 62;
            Windows.System.Power.PowerManager.Notify();
            Check(values[^1].Percent == 62 && values[^1].IsCharging, "Power events did not refresh data.");
            Check(Windows.System.Power.PowerManager.Subscribers == 3, "Power events were not subscribed once.");
            watch.Dispose();
            var count = values.Count;
            Windows.System.Power.PowerManager.Notify();
            Check(values.Count == count && Windows.System.Power.PowerManager.Subscribers == 0, "Power observers survived disposal.");
        });

        run("Windows network adapter falls back to local connectivity and follows the default network", () =>
        {
            var source = new WindowsSystemStatusSource(NullLogger<WindowsSystemStatusSource>.Instance);
            var values = new List<SystemStatusSnapshot>();
            Windows.Networking.Connectivity.NetworkInformation.Default = null;
            Windows.Networking.Connectivity.NetworkInformation.Profiles =
            [
                new() { Level = Windows.Networking.Connectivity.NetworkConnectivityLevel.None, ProfileName = "Offline" },
                new()
                {
                    Level = Windows.Networking.Connectivity.NetworkConnectivityLevel.LocalAccess,
                    ProfileName = "WiFi profile", IsWlanConnectionProfile = true,
                    WlanConnectionProfileDetails = new("Local SSID")
                }
            ];
            var watch = source.Watch(SystemStatusKind.Network, values.Add);
            Check(values[^1].Availability == SystemStatusAvailability.Available && values[^1].NetworkName == "Local SSID", "Local-only Wi-Fi was treated as offline.");
            Windows.Networking.Connectivity.NetworkInformation.Default = new()
            {
                Level = Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess,
                ProfileName = "Ethernet", NetworkAdapter = new() { IanaInterfaceType = 6 }
            };
            Windows.Networking.Connectivity.NetworkInformation.Notify();
            Check(values[^1].NetworkName == "Ethernet" && values[^1].NetworkKind == SystemNetworkKind.Ethernet, "Default route did not take precedence.");
            Windows.Networking.Connectivity.NetworkInformation.Default = null;
            Windows.Networking.Connectivity.NetworkInformation.Profiles = [];
            Windows.Networking.Connectivity.NetworkInformation.Notify();
            Check(values[^1].Availability == SystemStatusAvailability.Disconnected, "Disconnect retained the previous network name.");
            watch.Dispose();
            Check(Windows.Networking.Connectivity.NetworkInformation.Subscribers == 0, "Network observer survived disposal.");
        });

        run("Network types follow physical connections behind tunnels and adapter interface types", () =>
        {
            var source = new WindowsSystemStatusSource(NullLogger<WindowsSystemStatusSource>.Instance);
            var values = new List<SystemStatusSnapshot>();
            var tunnel = new Windows.Networking.Connectivity.ConnectionProfile
            {
                Level = Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess,
                ProfileName = "VPN", NetworkAdapter = new() { IanaInterfaceType = 53 }
            };
            Windows.Networking.Connectivity.NetworkInformation.Default = tunnel;
            Windows.Networking.Connectivity.NetworkInformation.Profiles =
            [
                tunnel,
                new()
                {
                    Level = Windows.Networking.Connectivity.NetworkConnectivityLevel.LocalAccess,
                    ProfileName = "Local virtual adapter", NetworkAdapter = new() { IanaInterfaceType = 6 }
                },
                new()
                {
                    Level = Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess,
                    ProfileName = "Wi-Fi profile", IsWlanConnectionProfile = true,
                    NetworkAdapter = new() { IanaInterfaceType = 71 },
                    WlanConnectionProfileDetails = new("Actual SSID")
                }
            ];
            using var watch = source.Watch(SystemStatusKind.Network, values.Add);
            Check(values[^1].NetworkKind == SystemNetworkKind.WiFi && values[^1].NetworkName == "Actual SSID",
                "Default tunnel hid the connected Wi-Fi network or selected a local-only virtual adapter.");

            foreach (var (type, expected) in new (uint Type, SystemNetworkKind Kind)[]
            {
                (6, SystemNetworkKind.Ethernet),
                (71, SystemNetworkKind.WiFi),
                (243, SystemNetworkKind.Cellular),
                (244, SystemNetworkKind.Cellular)
            })
            {
                Windows.Networking.Connectivity.NetworkInformation.Default = new()
                {
                    Level = Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess,
                    ProfileName = $"Adapter {type}", NetworkAdapter = new() { IanaInterfaceType = type }
                };
                Windows.Networking.Connectivity.NetworkInformation.Notify();
                Check(values[^1].NetworkKind == expected && values[^1].NetworkName == $"Adapter {type}",
                    $"Interface {type} was misidentified or a different network overrode the default connection.");
            }

            Windows.Networking.Connectivity.NetworkInformation.Default = tunnel;
            Windows.Networking.Connectivity.NetworkInformation.Profiles = [tunnel];
            Windows.Networking.Connectivity.NetworkInformation.Notify();
            Check(values[^1].NetworkKind == SystemNetworkKind.Unknown && values[^1].NetworkName == "VPN",
                "A tunnel without an identifiable underlying network was mislabeled as a physical connection.");
        });

        run("Windows Bluetooth adapter follows classic and LE connections, radio state, removal and failures", () =>
        {
            var radio = new Windows.Devices.Radios.Radio();
            Windows.Devices.Bluetooth.BluetoothAdapter.Read = () => Task.FromResult<Windows.Devices.Bluetooth.BluetoothAdapter?>(new() { Radio = radio });
            Windows.Devices.Enumeration.DeviceInformation.Watchers.Clear();
            var values = new List<SystemStatusSnapshot>();
            var source = new WindowsSystemStatusSource(NullLogger<WindowsSystemStatusSource>.Instance);
            var watch = source.Watch(SystemStatusKind.Bluetooth, values.Add);
            var watchers = Windows.Devices.Enumeration.DeviceInformation.Watchers;
            Check(watchers.Count == 2 && values[^1].Availability == SystemStatusAvailability.Available, "Bluetooth source failed to initialize.");
            var classic = new Windows.Devices.Enumeration.DeviceInformation("classic");
            classic.Properties["System.Devices.Aep.IsConnected"] = true;
            classic.Properties["System.Devices.Aep.ContainerId"] = "same-device";
            var ble = new Windows.Devices.Enumeration.DeviceInformation("ble");
            ble.Properties["System.Devices.Aep.IsConnected"] = true;
            ble.Properties["System.Devices.Aep.ContainerId"] = "same-device";
            watchers[0].Add(classic);
            watchers[1].Add(ble);
            Check(values[^1].ConnectedDeviceCount == 1, "Native Bluetooth adapters double-counted the same device.");
            radio.State = Windows.Devices.Radios.RadioState.Off;
            radio.Notify();
            Check(values[^1].Availability == SystemStatusAvailability.Disabled && values[^1].ConnectedDeviceCount == 0, "Powered-off radio retained connections.");
            radio.State = Windows.Devices.Radios.RadioState.On;
            radio.Notify();
            var update = new Windows.Devices.Enumeration.DeviceInformationUpdate("classic");
            update.Properties["System.Devices.Aep.IsConnected"] = false;
            watchers[0].Update(update);
            watchers[1].Remove("ble");
            Check(values[^1].ConnectedDeviceCount == 0, "Disconnect/remove retained a device.");
            watchers[0].Abort();
            Check(values[^1].Availability == SystemStatusAvailability.Unavailable, "Aborted watcher fabricated a connection count.");
            watch.Dispose();
            Check(watchers.All(watcher => watcher.Subscribers == 0) && radio.Subscribers == 0, "Bluetooth observers survived disposal.");
            Windows.Devices.Bluetooth.BluetoothAdapter.Read = () => Task.FromResult<Windows.Devices.Bluetooth.BluetoothAdapter?>(null);
            using var missing = source.Watch(SystemStatusKind.Bluetooth, values.Add);
            Check(values[^1].Availability == SystemStatusAvailability.Missing, "Missing Bluetooth hardware was not distinguished.");
        });

        run("Status observers are shared by kind and stale callbacks cannot revive released state", () =>
        {
            var source = new Source();
            using var service = CreateService(source);
            var first = service.Acquire(SystemStatusKind.Battery);
            var second = service.Acquire(SystemStatusKind.Battery);
            var network = service.Acquire(SystemStatusKind.Network);
            Check(source.Observers.Count == 2, "Observers were not shared by kind.");
            source.Observers[0].Emit(Available(45));
            first.Dispose();
            Check(!source.Observers[0].Disposed && service.GetState(SystemStatusKind.Battery).Percent == 45, "An active user's observer was released.");
            second.Dispose();
            Check(source.Observers[0].Disposed && service.GetState(SystemStatusKind.Battery) == SystemStatusSnapshot.Unavailable, "Last release retained state or resources.");
            using var replacement = service.Acquire(SystemStatusKind.Battery);
            source.Observers[0].Emit(Available(99));
            Check(service.GetState(SystemStatusKind.Battery) == SystemStatusSnapshot.Unavailable, "A stale observer overwrote the replacement.");
            source.Observers[2].Emit(Available(12));
            Check(service.GetState(SystemStatusKind.Battery).Percent == 12, "Replacement observer did not update.");
            service.Dispose();
            Check(source.Observers.All(observer => observer.Disposed), "Host shutdown leaked observers.");
            source.Observers[2].Emit(Available(55));
            Check(service.GetState(SystemStatusKind.Battery) == SystemStatusSnapshot.Unavailable, "A post-shutdown callback restored state.");
            network.Dispose();
        });

        run("Stopping during observer startup disposes the late observer and ignores its initial result", () =>
        {
            var source = new Source();
            using var service = CreateService(source);
            source.Starting = service.Dispose;
            using var lease = service.Acquire(SystemStatusKind.Volume);
            Check(source.Observers.Single().Disposed, "A startup racing shutdown leaked its observer.");
            source.Observers[0].Emit(Available(65));
            Check(service.GetState(SystemStatusKind.Volume) == SystemStatusSnapshot.Unavailable, "Late startup published after shutdown.");
        });

        run("A failed status kind does not block another kind and can retry after release", () =>
        {
            var source = new Source { FailKind = SystemStatusKind.Bluetooth };
            using var service = CreateService(source);
            using (service.Acquire(SystemStatusKind.Bluetooth))
            using (service.Acquire(SystemStatusKind.Battery))
            {
                source.Observers.Single().Emit(Available(77));
                Check(service.GetState(SystemStatusKind.Battery).Percent == 77, "A Bluetooth failure blocked battery updates.");
                Check(service.GetState(SystemStatusKind.Bluetooth) == SystemStatusSnapshot.Unavailable, "Failed source did not retain unavailable state.");
            }

            source.FailKind = null;
            using var retry = service.Acquire(SystemStatusKind.Bluetooth);
            Check(source.Observers.Count == 2, "Source did not retry on a later load.");
        });

        run("System status component handles every visibility combination, kind changes and repeat loading", () =>
        {
            var source = new Source();
            using var service = CreateService(source);
            var component = CreateComponent(service);
            component.Load();
            var root = (Grid)component.Content!;
            var icon = root.Children[0];
            var text = (TextBlock)root.Children[1];
            source.Observers[0].Emit(Available(70));
            foreach (var showIcon in new[] { true, false })
            foreach (var showText in new[] { true, false })
            {
                component.Settings.ShowIcon = showIcon;
                component.Settings.ShowText = showText;
                Check(icon.Visibility == (showIcon ? Visibility.Visible : Visibility.Collapsed), "Icon toggle ignored.");
                Check(text.Visibility == (showText ? Visibility.Visible : Visibility.Collapsed), "Text toggle ignored.");
                Check(root.ColumnSpacing == (showIcon && showText ? 6 : 0), "Hidden content retained a gap.");
            }

            component.Settings.Kind = SystemStatusKind.Volume;
            Check(source.Observers[0].Disposed && source.Observers.Count == 2, "Kind change retained the old observer.");
            source.Observers[0].Emit(Available(99));
            Check(text.Text == "状态不可用", "Old type callback changed the view.");
            source.Observers[1].Emit(Available(28));
            Check(text.Text == "28%", "New type did not refresh.");
            component.Unload();
            Check(source.Observers[1].Disposed, "Unload retained the observer.");
            component.Load();
            component.Load();
            Check(source.Observers.Count == 3, "Repeated load duplicated observers.");
            component.Unload();
        });

        run("Icon size stays independent of text size and refreshes after settings changes and reload", () =>
        {
            using var service = CreateService(new Source());
            var component = CreateComponent(service);
            component.Load();
            var icon = (FontIcon)((Grid)component.Content!).Children[0];
            component.FontSize = 40;
            Check(icon.FontSize == 24, "Text size changed the default icon size.");
            component.Settings.IconSize = 36;
            Check(icon.FontSize == 36 && component.FontSize == 40, "Icon size changed text size or did not refresh.");
            component.Unload();
            component.Settings.IconSize = 20;
            component.Load();
            Check(icon.FontSize == 20 && component.FontSize == 40, "Reload did not apply independent icon size.");
            component.Unload();
        });

        run("Status updates target each window and queued updates cannot change unloaded views", () =>
        {
            var source = new Source();
            using var service = CreateService(source);
            var firstWindow = Window.Current;
            var first = CreateComponent(service);
            first.Load();
            var secondWindow = new Window();
            Window.Current = secondWindow;
            var second = CreateComponent(service);
            second.Load();
            Check(source.Observers.Count == 1, "Separate windows started duplicate observers.");
            source.Observers[0].Emit(Available(81));
            Check(Text(second) == "81%" && Text(first) == "状态不可用", "Callback crossed dispatcher boundaries.");
            Window.Current = firstWindow;
            first.Dispatcher.Drain();
            Check(Text(first) == "81%", "First window did not receive its queued update.");
            Window.Current = secondWindow;
            source.Observers[0].Emit(Available(22));
            Window.Current = firstWindow;
            first.Unload();
            first.Dispatcher.Drain();
            Check(Text(first) == "81%", "Queued update mutated an unloaded view.");
            Window.Current = secondWindow;
            second.Unload();
            Window.Current = firstWindow;
        });
    }

    internal static async Task RunAsync(Func<string, Func<Task>, Task> runAsync)
    {
        await runAsync("Late Bluetooth initialization cannot subscribe after disposal or erase an earlier watcher failure", async () =>
        {
            var pending = new TaskCompletionSource<Windows.Devices.Bluetooth.BluetoothAdapter?>();
            Windows.Devices.Bluetooth.BluetoothAdapter.Read = () => pending.Task;
            Windows.Devices.Enumeration.DeviceInformation.Watchers.Clear();
            var values = new List<SystemStatusSnapshot>();
            var source = new WindowsSystemStatusSource(NullLogger<WindowsSystemStatusSource>.Instance);
            var radio = new Windows.Devices.Radios.Radio();
            var watch = source.Watch(SystemStatusKind.Bluetooth, values.Add);
            watch.Dispose();
            pending.SetResult(new() { Radio = radio });
            await Task.Delay(30);
            Check(radio.Subscribers == 0 && values.Count == 0,
                "Late radio initialization subscribed or published after disposal.");
            Check(Windows.Devices.Enumeration.DeviceInformation.Watchers.All(watcher => watcher.Subscribers == 0),
                "Early disposal retained device watchers.");

            pending = new TaskCompletionSource<Windows.Devices.Bluetooth.BluetoothAdapter?>();
            using var failed = source.Watch(SystemStatusKind.Bluetooth, values.Add);
            Windows.Devices.Enumeration.DeviceInformation.Watchers[^1].Abort();
            pending.SetResult(new() { Radio = radio });
            await Task.Delay(30);
            Check(values[^1].Availability == SystemStatusAvailability.Unavailable,
                "Late adapter initialization erased a native watcher failure.");
            Windows.Devices.Bluetooth.BluetoothAdapter.Read = () => Task.FromResult<Windows.Devices.Bluetooth.BluetoothAdapter?>(null);
        });
    }

    private static SystemStatusPresentation Display(SystemStatusKind kind, SystemStatusSnapshot state) => SystemStatusPresentation.Create(kind, state);
    private static SystemStatusSnapshot Available(int percent) => new() { Availability = SystemStatusAvailability.Available, Percent = percent };
    private static SystemStatusService CreateService(Source source) => new(source, NullLogger<SystemStatusService>.Instance);
    private static SystemStatusComponent CreateComponent(ISystemStatusService service)
    {
        var component = new SystemStatusComponent(service);
        component.SetSettings(new SystemStatusComponentSettings());
        return component;
    }

    private static string Text(SystemStatusComponent component) => ((TextBlock)((Grid)component.Content!).Children[1]).Text;
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class Source : ISystemStatusSource
    {
        public List<Observer> Observers { get; } = [];
        public Action? Starting { get; set; }
        public SystemStatusKind? FailKind { get; set; }
        public IDisposable Watch(SystemStatusKind kind, Action<SystemStatusSnapshot> publish)
        {
            if (kind == FailKind)
            {
                throw new InvalidOperationException("Simulated access denied.");
            }

            Starting?.Invoke();
            var observer = new Observer(publish);
            Observers.Add(observer);
            return observer;
        }
    }

    private sealed class Observer(Action<SystemStatusSnapshot> publish) : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Emit(SystemStatusSnapshot state) => publish(state);
        public void Dispose() => Disposed = true;
    }
}
