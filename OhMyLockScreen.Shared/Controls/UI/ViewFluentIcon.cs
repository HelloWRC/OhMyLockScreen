using FluentIcons.Common;
using FluentIcons.Uwp;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Shared.Controls.UI;

public sealed class ViewFluentIcon : FluentIcon
{
    private int _size;
    private FontFamily? _font;

    // FluentIcons caches FontFamily globally, but a lock-screen view can have its own UI thread.
    protected override FontFamily IconFont
    {
        get
        {
            var size = IconSize == IconSize.Resizable ? (IconVariant == IconVariant.Light ? 32 : 20) : (int)IconSize;
            if (_font == null || _size != size)
            {
                _font = new FontFamily($"ms-appx:///FluentIcons.Uwp/Assets/FluentSystemIcons-Size{size}.otf#Fluent System Icons Size{size}");
                _size = size;
            }
            return _font;
        }
    }
}
