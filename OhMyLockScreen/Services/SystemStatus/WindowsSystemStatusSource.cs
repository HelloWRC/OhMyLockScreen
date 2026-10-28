using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Abstractions.Services.SystemStatus;
using OhMyLockScreen.Shared.Models.SystemStatus;
using System.Reactive.Disposables;
using Windows.Networking.Connectivity;
using Windows.System.Power;

namespace OhMyLockScreen.Services.SystemStatus;

public sealed class WindowsSystemStatusSource(ILogger<WindowsSystemStatusSource> logger) : ISystemStatusSource
{
    public IDisposable Watch(SystemStatusKind kind, Action<SystemStatusSnapshot> publish)
    {
        void ReportFailure(Exception exception)
        {
            logger.LogWarning(exception, "无法读取系统状态 {Kind}", kind);
            publish(SystemStatusSnapshot.Unavailable);
        }

        if (kind == SystemStatusKind.Bluetooth)
        {
            return new BluetoothStatusObserver(publish, ReportFailure);
        }

        if (kind == SystemStatusKind.Volume)
        {
            return new AudioVolumeObserver(publish, ReportFailure);
        }

        var stopped = 0;
        void Refresh()
        {
            if (Volatile.Read(ref stopped) != 0)
            {
                return;
            }

            try
            {
                publish(kind == SystemStatusKind.Battery ? ReadBattery() : ReadNetwork());
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }

        if (kind == SystemStatusKind.Battery)
        {
            void Changed(object? sender, object args) => Refresh();
            PowerManager.BatteryStatusChanged += Changed;
            PowerManager.RemainingChargePercentChanged += Changed;
            PowerManager.PowerSupplyStatusChanged += Changed;
            Refresh();
            return Disposable.Create(() =>
            {
                Interlocked.Exchange(ref stopped, 1);
                PowerManager.BatteryStatusChanged -= Changed;
                PowerManager.RemainingChargePercentChanged -= Changed;
                PowerManager.PowerSupplyStatusChanged -= Changed;
            });
        }

        void NetworkChanged(object sender) => Refresh();
        NetworkInformation.NetworkStatusChanged += NetworkChanged;
        Refresh();
        return Disposable.Create(() =>
        {
            Interlocked.Exchange(ref stopped, 1);
            NetworkInformation.NetworkStatusChanged -= NetworkChanged;
        });
    }

    private static SystemStatusSnapshot ReadBattery() => new()
    {
        Availability = PowerManager.BatteryStatus == BatteryStatus.NotPresent
            ? SystemStatusAvailability.Missing : SystemStatusAvailability.Available,
        Percent = PowerManager.RemainingChargePercent,
        IsCharging = PowerManager.BatteryStatus == BatteryStatus.Charging
    };

    private static SystemStatusSnapshot ReadNetwork()
    {
        var profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile == null || profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None)
        {
            profile = null;
        }

        if (profile == null || GetNetworkKind(profile) == SystemNetworkKind.Unknown)
        {
            // A VPN/TUN can be the default route without describing the underlying connection.
            // Prefer an active, recognized connection; retain the default if none is available.
            var connected = NetworkInformation.GetConnectionProfiles()
                .Where(candidate => candidate.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None)
                .OrderByDescending(candidate => candidate.GetNetworkConnectivityLevel())
                .ToArray();
            profile = connected.FirstOrDefault(candidate => GetNetworkKind(candidate) != SystemNetworkKind.Unknown)
                ?? profile ?? connected.FirstOrDefault();
        }

        if (profile == null)
        {
            return new SystemStatusSnapshot { Availability = SystemStatusAvailability.Disconnected };
        }

        var name = profile.IsWlanConnectionProfile ? profile.WlanConnectionProfileDetails?.GetConnectedSsid() : null;
        return new SystemStatusSnapshot
        {
            Availability = SystemStatusAvailability.Available,
            NetworkName = string.IsNullOrWhiteSpace(name) ? profile.ProfileName : name,
            NetworkKind = GetNetworkKind(profile)
        };
    }

    private static SystemNetworkKind GetNetworkKind(ConnectionProfile profile)
    {
        if (profile.IsWlanConnectionProfile)
        {
            return SystemNetworkKind.WiFi;
        }

        if (profile.IsWwanConnectionProfile)
        {
            return SystemNetworkKind.Cellular;
        }

        return profile.NetworkAdapter?.IanaInterfaceType switch
        {
            6 => SystemNetworkKind.Ethernet,
            71 => SystemNetworkKind.WiFi,
            243 or 244 => SystemNetworkKind.Cellular,
            _ => SystemNetworkKind.Unknown
        };
    }
}
