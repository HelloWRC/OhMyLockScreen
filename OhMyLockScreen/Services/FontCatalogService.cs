using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas.Text;

namespace OhMyLockScreen.Services;

/// <summary>只缓存字体名称，避免跨视图共享 XAML 字体对象。</summary>
public sealed class FontCatalogService(ILogger<FontCatalogService> logger)
{
    private readonly Lazy<IReadOnlyList<string>> _fontFamilies = new(() => LoadFontFamilies(logger));

    public IReadOnlyList<string> FontFamilies => _fontFamilies.Value;

    private static IReadOnlyList<string> LoadFontFamilies(ILogger logger)
    {
        try
        {
            return CanvasTextFormat.GetSystemFontFamilies()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "无法枚举系统字体");
            return ["Microsoft YaHei UI", "Segoe UI"];
        }
    }
}
