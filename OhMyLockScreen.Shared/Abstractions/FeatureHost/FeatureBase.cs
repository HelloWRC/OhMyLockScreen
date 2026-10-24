using System.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;

namespace OhMyLockScreen.Shared.Abstractions.FeatureHost;

/// <summary>特性基类，设置在构造完成后由特性服务注入。</summary>
public abstract class FeatureBase : INotifyPropertyChanged, IDisposable
{
    private bool _isDisposed;

    internal IFeatureSettings? SettingsInternal { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void SetSettings(IFeatureSettings? settings)
    {
        if (ReferenceEquals(SettingsInternal, settings))
        {
            return;
        }

        SettingsInternal = settings;
        OnSettingsChanged();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Settings"));
    }

    /// <summary>自定义设置被注入或替换后调用，构造函数中不应访问设置。</summary>
    protected virtual void OnSettingsChanged()
    {
    }

    public abstract void Detach();

    internal virtual void AttachTo(object target)
    {
        throw new NotSupportedException("此特性未提供强类型目标的挂载入口。");
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        // 瞬态特性也由 DI 容器跟踪，宿主关闭时可能再次释放已卸载的实例。
        _isDisposed = true;
        Detach();
    }
}

/// <summary>提供强类型目标的特性基类，由调用方附加目标。</summary>
public abstract class FeatureBase<TTarget> : FeatureBase where TTarget : class
{
    public TTarget? Target { get; internal set; }

    public abstract void Attach(TTarget target);

    internal sealed override void AttachTo(object target)
    {
        if (target is not TTarget typedTarget)
        {
            throw new ArgumentException($"目标必须兼容 {typeof(TTarget).FullName}。", nameof(target));
        }

        Attach(typedTarget);
    }
}

public abstract class FeatureBase<TTarget, TSettings> : FeatureBase<TTarget>
    where TTarget : class
    where TSettings : class, IFeatureSettings
{
    public TSettings Settings => SettingsInternal as TSettings
        ?? throw new InvalidOperationException("特性设置尚未初始化。");
}
