using OhMyLockScreen.Shared.Abstractions.Models.Scene;

namespace OhMyLockScreen.Shared.Abstractions.Controls.Scene;

/// <summary>组件控件基类。设置在构造完成后、加入视觉树前由组件服务注入。</summary>
public abstract class ComponentBase : ControlWithSettingsBase;

/// <summary>提供强类型自定义设置的组件基类。</summary>
public abstract class ComponentBase<TSettings> : ComponentBase
    where TSettings : class, IComponentSettings, new()
{
    public TSettings Settings => SettingsInternal as TSettings
        ?? throw new InvalidOperationException("组件设置尚未初始化。");
}
