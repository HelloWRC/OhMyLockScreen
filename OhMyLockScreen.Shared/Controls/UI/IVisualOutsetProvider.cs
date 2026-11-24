namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>向基于 LayerVisual 的外层装饰器声明内容向四周外扩的渲染边距（DIP）。</summary>
public interface IVisualOutsetProvider
{
    double VisualOutset { get; }

    event EventHandler? VisualOutsetChanged;
}
