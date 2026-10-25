using Windows.Foundation;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.Scene;

/// <summary>保留 Grid 的行列布局，同时让自动尺寸的父容器容纳网格内容。</summary>
public sealed class AutoSizedGrid : Grid
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var desiredSize = base.MeasureOverride(availableSize);
        var contentWidth = 0.0;
        var contentHeight = 0.0;
        foreach (var child in Children)
        {
            contentWidth = Math.Max(contentWidth, child.DesiredSize.Width);
            contentHeight = Math.Max(contentHeight, child.DesiredSize.Height);
        }

        return new Size(
            Math.Max(desiredSize.Width, contentWidth),
            Math.Max(desiredSize.Height, contentHeight));
    }
}
