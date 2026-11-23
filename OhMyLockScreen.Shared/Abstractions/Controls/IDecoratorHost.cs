using OhMyLockScreen.Shared.Controls.UI;

namespace OhMyLockScreen.Shared.Abstractions.Controls;

/// <summary>公开支持特性动态注册单子节点链式装饰器的宿主。</summary>
public interface IDecoratorHost
{
    DecoratorChain Decorators { get; }
}
