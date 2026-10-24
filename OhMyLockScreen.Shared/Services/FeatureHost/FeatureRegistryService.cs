using OhMyLockScreen.Shared.Models.FeatureHost;

namespace OhMyLockScreen.Shared.Services.FeatureHost;

/// <summary>当前宿主内的特性注册表。</summary>
public sealed class FeatureRegistryService
{
    private readonly Dictionary<string, FeatureRegisterInfo> _features;

    public FeatureRegistryService(IEnumerable<FeatureRegisterInfo> features)
    {
        var registered = features.ToArray();
        Registered = Array.AsReadOnly(registered);
        _features = registered.ToDictionary(feature => feature.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<FeatureRegisterInfo> Registered { get; }

    public FeatureRegisterInfo? Find(string id) => _features.GetValueOrDefault(id);
}
