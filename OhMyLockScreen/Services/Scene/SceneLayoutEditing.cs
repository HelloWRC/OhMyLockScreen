using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Services.Scene;
/// <summary>直接修改原生布局参数，不引入与布局分离的位置状态。</summary>
internal static class SceneLayoutEditing
{
    internal static bool ReordersChildrenOnCanvasDrop(Panel panel) =>
        panel is StackPanel or CommunityToolkit.WinUI.Controls.WrapPanel;

    internal static LayoutFeatureSettings? GetLayout(IFeatureContainer? container, bool create)
    {
        if (container == null)
        {
            return null;
        }

        var feature = container.Features.FirstOrDefault(item => item.Id == "omls.features.layout.layout");
        if (feature == null && create)
        {
            feature = new FeatureInfo
            {
                Id = "omls.features.layout.layout",
                Settings = new LayoutFeatureSettings()
            };
            container.Features.Add(feature);
        }

        return feature?.GetSettingsOrCreate<LayoutFeatureSettings>();
    }

    internal static void ApplyDropPosition(ComponentSettings component, Panel panel, Point point)
    {
        if (panel is not Grid and not Canvas)
        {
            return;
        }

        var gridSettings = component.Features.FirstOrDefault(feature => feature.Id == "omls.features.layout.grid")
            ?.TryGetSettings<GridLayoutFeatureSettings>();
        var area = GetLayoutArea(panel, gridSettings?.Row ?? 0, gridSettings?.Column ?? 0,
            gridSettings?.RowSpan ?? 1, gridSettings?.ColumnSpan ?? 1);
        var layout = GetLayout(component, create: true)!;
        layout.HorizontalAlignment = HorizontalAlignment.Left;
        layout.VerticalAlignment = VerticalAlignment.Top;
        layout.Margin = new Thickness(Round(point.X - area.X), Round(point.Y - area.Y), 0, 0);
    }

    internal static void AlignDropPosition(ComponentSettings component, Panel panel, Rect bounds)
    {
        if (panel is not Grid || GetLayout(component, create: false) is not { IsAutoAlignment: true } layout)
        {
            return;
        }

        var gridSettings = component.Features.FirstOrDefault(feature => feature.Id == "omls.features.layout.grid")
            ?.TryGetSettings<GridLayoutFeatureSettings>();
        var area = GetLayoutArea(panel, gridSettings?.Row ?? 0, gridSettings?.Column ?? 0,
            gridSettings?.RowSpan ?? 1, gridSettings?.ColumnSpan ?? 1);
        ApplyPosition(layout, bounds.X - area.X, bounds.Y - area.Y, bounds.Width, bounds.Height,
            area.Width, area.Height, resize: false, canvas: false, autoAlignment: true);
    }

    internal static Rect GetLayoutArea(Panel panel, int row, int column, int rowSpan, int columnSpan)
    {
        if (panel is not Grid grid)
        {
            return new Rect(0, 0, panel.ActualWidth, panel.ActualHeight);
        }

        var columns = grid.ColumnDefinitions.Select(definition => definition.ActualWidth).ToArray();
        var rows = grid.RowDefinitions.Select(definition => definition.ActualHeight).ToArray();
        var (x, width) = GetAxisArea(columns, column, columnSpan, grid.ColumnSpacing, grid.ActualWidth);
        var (y, height) = GetAxisArea(rows, row, rowSpan, grid.RowSpacing, grid.ActualHeight);
        return new Rect(x, y, width, height);
    }

    private static (double Start, double Length) GetAxisArea(double[] lengths, int index, int span, double spacing, double fallback)
    {
        if (lengths.Length == 0)
        {
            return (0, fallback);
        }

        index = Math.Clamp(index, 0, lengths.Length - 1);
        span = Math.Clamp(span, 1, lengths.Length - index);
        return (lengths.Take(index).Sum() + index * spacing, lengths.Skip(index).Take(span).Sum() + (span - 1) * spacing);
    }

    internal static double Round(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }

    internal static void ApplyBounds(
        LayoutFeatureSettings settings,
        double x,
        double y,
        double width,
        double height,
        double availableWidth,
        double availableHeight,
        bool resize,
        bool canvas = false,
        bool autoAlignment = true)
    {
        settings.Width = Round(resize ? width : settings.Width);
        settings.Height = Round(resize ? height : settings.Height);
        if (resize)
        {
            settings.IsAutoWidth = false;
            settings.IsAutoHeight = false;
        }

        ApplyPosition(settings, x, y, width, height, availableWidth, availableHeight, resize, canvas, autoAlignment);
    }

    private static void ApplyPosition(
        LayoutFeatureSettings settings,
        double x,
        double y,
        double width,
        double height,
        double availableWidth,
        double availableHeight,
        bool resize,
        bool canvas,
        bool autoAlignment)
    {
        var horizontalAlignment = canvas
            ? HorizontalAlignment.Left
            : autoAlignment
                ? GetNearestHorizontalAlignment(x, width, availableWidth, includeCenter: !resize)
                : settings.HorizontalAlignment;
        var verticalAlignment = canvas
            ? VerticalAlignment.Top
            : autoAlignment
                ? GetNearestVerticalAlignment(y, height, availableHeight, includeCenter: !resize)
                : settings.VerticalAlignment;
        var (left, right) = GetHorizontalMargins(horizontalAlignment, x, width, availableWidth);
        var (top, bottom) = GetVerticalMargins(verticalAlignment, y, height, availableHeight);

        settings.HorizontalAlignment = horizontalAlignment;
        settings.VerticalAlignment = verticalAlignment;
        settings.Margin = new Thickness(left, top, right, bottom);
    }

    private static HorizontalAlignment GetNearestHorizontalAlignment(
        double x,
        double width,
        double availableWidth,
        bool includeCenter)
    {
        var center = (availableWidth - width) / 2;
        var leftDistance = Math.Abs(x);
        var centerDistance = Math.Abs(x - center);
        var rightDistance = Math.Abs(availableWidth - x - width);
        if (includeCenter && centerDistance <= leftDistance && centerDistance <= rightDistance)
        {
            return HorizontalAlignment.Center;
        }

        return leftDistance <= rightDistance ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private static VerticalAlignment GetNearestVerticalAlignment(
        double y,
        double height,
        double availableHeight,
        bool includeCenter)
    {
        var center = (availableHeight - height) / 2;
        var topDistance = Math.Abs(y);
        var centerDistance = Math.Abs(y - center);
        var bottomDistance = Math.Abs(availableHeight - y - height);
        if (includeCenter && centerDistance <= topDistance && centerDistance <= bottomDistance)
        {
            return VerticalAlignment.Center;
        }

        return topDistance <= bottomDistance ? VerticalAlignment.Top : VerticalAlignment.Bottom;
    }

    private static (double Left, double Right) GetHorizontalMargins(
        HorizontalAlignment alignment,
        double x,
        double width,
        double availableWidth)
    {
        return alignment switch
        {
            HorizontalAlignment.Center => GetCenteredMargins(x, width, availableWidth),
            HorizontalAlignment.Right => (0, Round(availableWidth - x - width)),
            HorizontalAlignment.Stretch => (Round(x), Round(availableWidth - x - width)),
            _ => (Round(x), 0)
        };
    }

    private static (double Top, double Bottom) GetVerticalMargins(
        VerticalAlignment alignment,
        double y,
        double height,
        double availableHeight)
    {
        return alignment switch
        {
            VerticalAlignment.Center => GetCenteredMargins(y, height, availableHeight),
            VerticalAlignment.Bottom => (0, Round(availableHeight - y - height)),
            VerticalAlignment.Stretch => (Round(y), Round(availableHeight - y - height)),
            _ => (Round(y), 0)
        };
    }

    private static (double Leading, double Trailing) GetCenteredMargins(double position, double size, double available)
    {
        var offset = position - (available - size) / 2;
        return offset < 0
            ? (0, Round(-offset * 2))
            : (Round(offset * 2), 0);
    }

    internal static void ResizeInStack(LayoutFeatureSettings settings, double width, double height)
    {
        settings.Width = Round(width);
        settings.Height = Round(height);
        settings.IsAutoWidth = false;
        settings.IsAutoHeight = false;
        var margin = settings.Margin;
        settings.Margin = new Thickness(Round(margin.Left), Round(margin.Top), Round(margin.Right), Round(margin.Bottom));
    }

    internal static (double X, double Y) ConstrainToDominantAxis(double startX, double startY, double currentX, double currentY)
    {
        var deltaX = currentX - startX;
        var deltaY = currentY - startY;
        return Math.Abs(deltaX) >= Math.Abs(deltaY)
            ? (currentX, startY)
            : (startX, currentY);
    }

    internal static int GetInsertionIndex(
        IReadOnlyList<(double X, double Y, double Width, double Height)> bounds,
        double pointerX,
        double pointerY,
        bool? horizontal,
        bool wrap = false)
    {
        if (horizontal is { } axis && !wrap)
        {
            var coordinate = axis ? pointerX : pointerY;
            return bounds.Count(bound => coordinate > (axis ? bound.X + bound.Width / 2 : bound.Y + bound.Height / 2));
        }

        var nearestIndex = 0;
        var nearestDistance = double.MaxValue;
        var after = false;
        for (var index = 0; index < bounds.Count; index++)
        {
            var bound = bounds[index];
            var deltaX = Math.Max(Math.Max(bound.X - pointerX, pointerX - bound.X - bound.Width), 0);
            var deltaY = Math.Max(Math.Max(bound.Y - pointerY, pointerY - bound.Y - bound.Height), 0);
            var distance = deltaX * deltaX + deltaY * deltaY;
            if (distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            nearestIndex = index;
            var centerX = bound.X + bound.Width / 2;
            var centerY = bound.Y + bound.Height / 2;
            after = horizontal switch
            {
                true => pointerX >= centerX,
                false => pointerY >= centerY,
                _ => Math.Abs(pointerX - centerX) >= Math.Abs(pointerY - centerY)
                    ? pointerX >= centerX
                    : pointerY >= centerY
            };
        }

        return nearestIndex + (after ? 1 : 0);
    }
}
