using System.Collections.ObjectModel;
using OhMyLockScreen.Shared.Models.Scene;

namespace OhMyLockScreen.Shared.Abstractions.Models.Scene;

/// <summary>容器组件的自定义设置；具体容器负责通过 Presenter 显示子组件。</summary>
public interface IComponentContainerSettings : IComponentSettings
{
    public ObservableCollection<ComponentSettings> Children { get; set; }
}
