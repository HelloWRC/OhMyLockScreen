using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Attributes.Scene;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Controls.Scene;

/// <summary>展示自定义文本的最小示例组件。</summary>
[ComponentInfo(Id, "文本", "\U000F07F0", "显示自定义文本。")]
public sealed class TextComponent : ComponentBase<TextComponentSettings>
{
    public const string Id = "OhMyLockScreen.Text";

    private readonly TextBlock _text = new();

    public TextComponent()
    {
        Content = _text;
    }

    protected override void OnSettingsChanged()
    {
        _text.SetBinding(TextBlock.TextProperty, new Binding
        {
            Source = Settings,
            Path = new PropertyPath(nameof(TextComponentSettings.Text)),
            Mode = BindingMode.OneWay
        });
        _text.SetBinding(TextBlock.FontSizeProperty, new Binding
        {
            Source = Settings,
            Path = new PropertyPath(nameof(TextComponentSettings.FontSize)),
            Mode = BindingMode.OneWay
        });
    }
}
