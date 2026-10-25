using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Scene;

namespace OhMyLockScreen.Controls.Components;

[ContainerComponent]
[ComponentInfo(Id, "布局容器", FluentIcons.Common.Icon.Group, "组合组件，并使用网格、堆叠、环绕或画布布局。")]
public sealed partial class ContainerComponent : ContainerComponentBase
{
    public const string Id = "omls.components.container";
    public ContainerComponent() => InitializeComponent();
    protected override void OnSettingsChanged() => Bindings.Update();
}