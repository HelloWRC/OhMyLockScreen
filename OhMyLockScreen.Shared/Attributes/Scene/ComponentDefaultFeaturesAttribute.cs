namespace OhMyLockScreen.Shared.Attributes.Scene;

/// <summary>指定组件默认特性的提供器；提供器必须实现 IComponentDefaultFeaturesProvider 并具有公开无参构造函数。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComponentDefaultFeaturesAttribute : Attribute
{
    public ComponentDefaultFeaturesAttribute(Type providerType)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        ProviderType = providerType;
    }

    public Type ProviderType { get; }
}
