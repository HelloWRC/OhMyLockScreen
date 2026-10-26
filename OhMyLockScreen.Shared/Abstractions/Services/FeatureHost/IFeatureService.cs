using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;

namespace OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;

public interface IFeatureService
{
    /// <summary>创建独立特性并注入设置，由调用方附加目标；未注册的 Id 返回空。</summary>
    FeatureBase? CreateFeature(FeatureInfo settings);

    /// <summary>在当前视图的 UI 线程创建独立设置控件并注入设置，同时以设置模型作为 DataContext；未注册 Id 或设置控件时返回空。</summary>
    FeatureSettingsControlBase? CreateSettingsControl(FeatureInfo settings);
}
