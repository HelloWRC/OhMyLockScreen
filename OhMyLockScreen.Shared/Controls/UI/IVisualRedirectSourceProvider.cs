using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>为外层基于 RedirectVisual 的装饰器提供未被内部遮罩隐藏的真实视觉源元素。</summary>
public interface IVisualRedirectSourceProvider
{
    FrameworkElement? VisualRedirectSource { get; }

    event EventHandler? VisualRedirectSourceChanged;
}
