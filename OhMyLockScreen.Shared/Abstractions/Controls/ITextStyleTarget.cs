using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Abstractions.Controls;

/// <summary>公开组件中接受行高、文本对齐和装饰线的文本元素。</summary>
public interface ITextStyleTarget
{
    FrameworkElement TextElement { get; }
}
