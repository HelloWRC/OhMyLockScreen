using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.Scene;

/// <summary>保留 Canvas 定位语义，同时让自动尺寸的父容器容纳画布内容。</summary>
public sealed class AutoSizedCanvas : Canvas
{
    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);

        var width = 0.0;
        var height = 0.0;
        foreach (var child in Children)
        {
            var left = Canvas.GetLeft(child);
            var top = Canvas.GetTop(child);
            if (double.IsNaN(left))
            {
                left = 0;
            }

            if (double.IsNaN(top))
            {
                top = 0;
            }

            width = Math.Max(width, left + child.DesiredSize.Width);
            height = Math.Max(height, top + child.DesiredSize.Height);
        }

        return new Size(width, height);
    }
}
