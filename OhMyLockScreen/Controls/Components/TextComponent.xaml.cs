using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Scene;

namespace OhMyLockScreen.Controls.Components;
/// <summary>展示自定义文本的最小示例组件。</summary>
[ComponentInfo(Id, "文本", FluentIcons.Common.Icon.Text, "显示自定义文本。")]
public sealed partial class TextComponent : TextComponentBase
{
    public const string Id = "omls.components.text";
    public TextComponent() => InitializeComponent();
    protected override void OnSettingsChanged() => Bindings.Update();
}