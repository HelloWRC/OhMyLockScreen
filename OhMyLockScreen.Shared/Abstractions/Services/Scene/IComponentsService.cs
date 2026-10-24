using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Models.Scene;

namespace OhMyLockScreen.Shared.Abstractions.Services.Scene;

public interface IComponentsService
{
    /// <summary>在当前视图的 UI 线程创建独立组件；未注册的 Id 返回空。</summary>
    public ComponentBase? CreateComponent(ComponentSettings settings);
}
