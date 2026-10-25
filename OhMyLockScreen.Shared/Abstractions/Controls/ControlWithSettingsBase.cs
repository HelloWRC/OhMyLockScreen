using System.ComponentModel;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Abstractions.Controls;
/// <summary>支持设置注入的控件基类。设置在构造完成后、加入视觉树前由服务注入。</summary>
public abstract class ControlWithSettingsBase : UserControl, INotifyPropertyChanged
{
    internal object? SettingsInternal
    {
        get; private set;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal void SetSettings(object? settings)
    {
        if (ReferenceEquals(SettingsInternal, settings))
        {
            return;
        }

        SettingsInternal = settings;
        OnSettingsChanged();
        OnPropertyChanged("Settings");
    }

    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    /// <summary>设置被注入或替换后调用。构造函数中不应访问设置。</summary>
    protected virtual void OnSettingsChanged()
    {
    }
}

/// <summary>提供强类型设置的控件基类。</summary>
public abstract class ControlWithSettingsBase<TSettings> : ControlWithSettingsBase where TSettings : class
{
    public TSettings Settings => SettingsInternal as TSettings ?? throw new InvalidOperationException("控件设置尚未初始化。");
}