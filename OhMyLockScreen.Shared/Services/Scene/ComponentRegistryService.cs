using OhMyLockScreen.Shared.Models.Scene;

namespace OhMyLockScreen.Shared.Services.Scene;

/// <summary>当前宿主内的组件注册表。</summary>
public sealed class ComponentRegistryService
{
    private readonly Dictionary<string, ComponentRegisterInfo> _components;

    public ComponentRegistryService(IEnumerable<ComponentRegisterInfo> components)
    {
        var registered = components.ToArray();
        Registered = Array.AsReadOnly(registered);
        _components = registered.ToDictionary(component => component.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<ComponentRegisterInfo> Registered { get; }

    public ComponentRegisterInfo? Find(string id) => _components.GetValueOrDefault(id);
}
