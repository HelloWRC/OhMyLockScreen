// Adapted from ClassIsland.Core.Controls.Empty, GPL-3.0.
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>
/// 空白内容占位符。
/// </summary>
public sealed class Empty : Control
{
    private static readonly Uri DefaultIconUri = new("ms-appx:///Assets/Stickers/水月_疑惑.webp");

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(IconSource),
        typeof(Empty),
        new PropertyMetadata(null));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(Empty),
        new PropertyMetadata("啥都没有"));

    public static readonly DependencyProperty IconHeightProperty = DependencyProperty.Register(
        nameof(IconHeight),
        typeof(double),
        typeof(Empty),
        new PropertyMetadata(64.0));

    public static readonly DependencyProperty IconWidthProperty = DependencyProperty.Register(
        nameof(IconWidth),
        typeof(double),
        typeof(Empty),
        new PropertyMetadata(64.0));

    public Empty()
    {
        DefaultStyleKey = typeof(Empty);
        // IconSource is a DependencyObject with UI-thread affinity; keep one instance per control instead of static metadata.
        Icon = new BitmapIconSource
        {
            UriSource = DefaultIconUri,
            ShowAsMonochrome = false,
        };
    }

    public IconSource? Icon
    {
        get => (IconSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double IconHeight
    {
        get => (double)GetValue(IconHeightProperty)!;
        set => SetValue(IconHeightProperty, value);
    }

    public double IconWidth
    {
        get => (double)GetValue(IconWidthProperty)!;
        set => SetValue(IconWidthProperty, value);
    }
}
