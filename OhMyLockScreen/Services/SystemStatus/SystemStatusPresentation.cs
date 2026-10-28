using FluentIcons.Common;
using OhMyLockScreen.Shared.Models.SystemStatus;

namespace OhMyLockScreen.Services.SystemStatus;

public sealed record SystemStatusPresentation(Icon Icon, string Text)
{
    public static SystemStatusPresentation Create(SystemStatusKind kind, SystemStatusSnapshot state)
    {
        var available = state.Availability == SystemStatusAvailability.Available;
        var percent = Math.Clamp(state.Percent, 0, 100);
        var icon = kind switch
        {
            SystemStatusKind.Battery when !available => Icon.BatteryWarning,
            SystemStatusKind.Battery when state.IsCharging => BatteryIcon(percent, true),
            SystemStatusKind.Battery => BatteryIcon(percent, false),
            SystemStatusKind.Network when state.Availability == SystemStatusAvailability.Unavailable => Icon.GlobeWarning,
            SystemStatusKind.Network when !available => Icon.GlobeOff,
            SystemStatusKind.Network => state.NetworkKind switch
            {
                SystemNetworkKind.WiFi => Icon.WiFi,
                SystemNetworkKind.Cellular => Icon.CellularData5,
                SystemNetworkKind.Ethernet => Icon.Connector,
                _ => Icon.Globe
            },
            SystemStatusKind.Volume when !available || state.IsMuted => Icon.SpeakerMute,
            SystemStatusKind.Volume => percent == 0 ? Icon.Speaker0 : percent <= 33 ? Icon.Speaker1 : Icon.Speaker2,
            SystemStatusKind.Bluetooth when state.Availability is SystemStatusAvailability.Disabled or SystemStatusAvailability.Missing => Icon.BluetoothDisabled,
            SystemStatusKind.Bluetooth when available && state.ConnectedDeviceCount > 0 => Icon.BluetoothConnected,
            _ => Icon.Bluetooth
        };
        var text = state.Availability == SystemStatusAvailability.Unavailable ? "状态不可用" : kind switch
        {
            SystemStatusKind.Battery => available ? $"{percent}%" : "无电池",
            SystemStatusKind.Network => available
                ? string.IsNullOrWhiteSpace(state.NetworkName) ? "已连接网络" : state.NetworkName
                : "未连接网络",
            SystemStatusKind.Volume => available ? $"{percent}%" : "无音频输出设备",
            SystemStatusKind.Bluetooth => state.Availability switch
            {
                SystemStatusAvailability.Missing => "无蓝牙适配器",
                SystemStatusAvailability.Disabled => "蓝牙已关闭",
                _ => state.ConnectedDeviceCount > 0 ? $"已连接 {state.ConnectedDeviceCount} 台设备" : "未连接设备"
            },
            _ => "状态不可用"
        };
        return new SystemStatusPresentation(icon, text);
    }

    private static Icon BatteryIcon(int percent, bool charging)
    {
        // Resolve by semantic enum name, never font code points or enum arithmetic.
        var level = (int)Math.Round(percent / 10d, MidpointRounding.AwayFromZero);
        return Enum.Parse<Icon>($"Battery{(charging ? "Charge" : "")}{level}");
    }
}
