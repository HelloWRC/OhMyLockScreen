using OhMyLockScreen.Shared.Models.FeatureHost;

namespace OhMyLockScreen.Shared.Abstractions.Models.Scene;

/// <summary>为新建组件提供默认特性配置，不依赖组件控件实例。</summary>
public interface IComponentDefaultFeaturesProvider
{
    /// <summary>每次调用应按声明顺序返回独立的特性配置及其设置对象，不复用可变对象。</summary>
    IEnumerable<FeatureInfo> CreateDefaultFeatures();
}
