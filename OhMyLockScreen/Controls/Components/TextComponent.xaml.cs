using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Controls.Components;
/// <summary>展示自定义文本的最小示例组件。</summary>
[ComponentInfo(Id, "文本", FluentIcons.Common.Icon.Text, "显示自定义文本。")]
[ComponentDefaultFeatures(typeof(TextDefaultFeaturesProvider))]
public sealed partial class TextComponent : TextComponentBase, ITextStyleTarget
{
    public const string Id = "omls.components.text";
    public FrameworkElement TextElement => TextContent;
    public TextComponent() => InitializeComponent();
    protected override void OnSettingsChanged() => Bindings.Update();
}
