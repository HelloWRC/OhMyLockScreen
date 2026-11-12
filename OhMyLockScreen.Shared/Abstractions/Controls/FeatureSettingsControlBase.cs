using OhMyLockScreen.Shared.Controls.Scene;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Abstractions.Controls;

/// <summary>支持设置注入和编辑目标的特性设置控件基类。</summary>
public abstract class FeatureSettingsControlBase : ControlWithSettingsBase
{
    public static readonly DependencyProperty BlobUriConverterProperty = DependencyProperty.Register(
        nameof(BlobUriConverter), typeof(SceneBlobUriConverter), typeof(FeatureSettingsControlBase), new PropertyMetadata(null));

    public SceneBlobUriConverter? BlobUriConverter
    {
        get => (SceneBlobUriConverter?)GetValue(BlobUriConverterProperty);
        set => SetValue(BlobUriConverterProperty, value);
    }

    public static readonly DependencyProperty TargetElementProperty = DependencyProperty.Register(
        nameof(TargetElement), typeof(FrameworkElement), typeof(FeatureSettingsControlBase), new PropertyMetadata(null));

    public FrameworkElement? TargetElement
    {
        get => (FrameworkElement?)GetValue(TargetElementProperty);
        set => SetValue(TargetElementProperty, value);
    }
}

/// <summary>提供强类型设置的特性设置控件基类。</summary>
public abstract class FeatureSettingsControlBase<TSettings> : FeatureSettingsControlBase where TSettings : class
{
    public TSettings Settings => SettingsInternal as TSettings ?? throw new InvalidOperationException("控件设置尚未初始化。");
}
