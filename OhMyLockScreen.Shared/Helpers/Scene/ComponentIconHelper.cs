using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Helpers.Scene;

internal static class ComponentIconHelper
{
    internal static FontIconSource? CreateIconSource(string? glyph)
    {
        if (string.IsNullOrEmpty(glyph))
        {
            return null;
        }

        // FluentIcons 控件会自动更新 Glyph；只复用其默认字体，
        // 使用普通图标源保留传入的原始字元。
        return new FontIconSource
        {
            Glyph = glyph,
            FontFamily = new FluentIcons.Uwp.FluentIconSource().FontFamily
        };
    }
}
