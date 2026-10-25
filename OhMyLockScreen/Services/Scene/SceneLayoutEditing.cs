using OhMyLockScreen.Models.Features.Layout;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Services.Scene;
/// <summary>直接修改原生布局参数，不引入与布局分离的位置状态。</summary>
internal static class SceneLayoutEditing
{
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
