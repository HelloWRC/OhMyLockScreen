namespace OhMyLockScreen.Shared.Models.SystemStatus;

public enum SystemStatusKind
{
    Battery,
    Network,
    Volume,
    Bluetooth
}

public enum SystemStatusAvailability
{
    Unavailable,
    Available,
    Missing,
    Disconnected,
    Disabled
}

public enum SystemNetworkKind
{
    Unknown,
    WiFi,
    Ethernet,
    Cellular
}

/// <summary>Immutable system data; contains no dispatcher-bound resources.</summary>
public sealed record SystemStatusSnapshot
{
    public static SystemStatusSnapshot Unavailable { get; } = new();
    public SystemStatusAvailability Availability { get; init; }
    public int Percent { get; init; }
    public bool IsCharging { get; init; }
    public bool IsMuted { get; init; }
    public string NetworkName { get; init; } = "";
    public SystemNetworkKind NetworkKind { get; init; }
    public int ConnectedDeviceCount { get; init; }
}

public sealed class SystemStatusChangedEventArgs(SystemStatusKind kind) : EventArgs
{
    public SystemStatusKind Kind { get; } = kind;
}

/// <summary>Association endpoints can describe the same physical Bluetooth device more than once.</summary>
public sealed record BluetoothDeviceIdentity(string Id, string? ContainerId, string? Address, bool IsConnected)
{
    public static int CountConnected(IEnumerable<BluetoothDeviceIdentity> devices)
    {
        var groups = new List<HashSet<string>>();
        foreach (var device in devices.Where(device => device.IsConnected))
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var container = device.ContainerId?.Trim();
            if (Guid.TryParse(container, out var guid))
            {
                container = guid == Guid.Empty ? null : guid.ToString("N");
            }

            if (!string.IsNullOrWhiteSpace(container))
            {
                keys.Add("container:" + container);
            }

            if (!string.IsNullOrWhiteSpace(device.Address))
            {
                keys.Add("address:" + string.Concat(device.Address.Where(char.IsLetterOrDigit)));
            }

            if (keys.Count == 0)
            {
                keys.Add("id:" + device.Id);
            }

            // Merge aliases transitively: one endpoint may expose only an address,
            // while another supplies both that address and the physical container.
            for (var index = groups.Count - 1; index >= 0; --index)
            {
                if (groups[index].Overlaps(keys))
                {
                    keys.UnionWith(groups[index]);
                    groups.RemoveAt(index);
                }
            }

            groups.Add(keys);
        }

        return groups.Count;
    }
}
