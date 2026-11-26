using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>向基于 LayerVisual 或 VisualSurface 的外层装饰器声明内容向四周外扩的渲染边距（DIP）。</summary>
public interface IVisualOutsetProvider
{
    double VisualOutset { get; }

    Thickness VisualOutsetThickness => new(VisualOutset);

    event EventHandler? VisualOutsetChanged;
}
