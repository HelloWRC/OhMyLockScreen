using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Abstractions.Controls.Scene;

/// <summary>组件控件基类。设置在构造完成后、加入视觉树前由组件服务注入。</summary>
public abstract class ComponentBase : ControlWithSettingsBase
{
    public static readonly DependencyProperty BlobUriConverterProperty = DependencyProperty.Register(
        nameof(BlobUriConverter), typeof(SceneBlobUriConverter), typeof(ComponentBase),
        new PropertyMetadata(null, (sender, _) => ((ComponentBase)sender).OnPropertyChanged(nameof(BlobUriConverter))));

    public SceneBlobUriConverter? BlobUriConverter
    {
        get => (SceneBlobUriConverter?)GetValue(BlobUriConverterProperty);
        internal set => SetValue(BlobUriConverterProperty, value);
    }

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing), typeof(bool), typeof(ComponentBase), new PropertyMetadata(false, OnEditingChanged));

    public bool IsEditing
    {
        get => GetValue(IsEditingProperty) is true;
        set => SetValue(IsEditingProperty, value);
    }

    private static void OnEditingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var component = (ComponentBase)sender;
        component.OnPropertyChanged(nameof(IsEditing));
        component.OnIsEditingChanged();
    }

    protected virtual void OnIsEditingChanged()
    {
    }
}

/// <summary>提供强类型自定义设置的组件基类。</summary>
public abstract class ComponentBase<TSettings> : ComponentBase
    where TSettings : class, IComponentSettings, new()
{
    public TSettings Settings => SettingsInternal as TSettings
        ?? throw new InvalidOperationException("组件设置尚未初始化。");
}
