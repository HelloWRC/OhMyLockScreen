using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using FluentIcons.Common;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Models.Weather;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace OhMyLockScreen.Shared.Controls.Weather;

public sealed class WeatherAlertBadge : UserControl
{
    private static readonly HttpClient FallbackHttp = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private CancellationTokenSource? _loadCancellation;
    private bool _httpFallbackStarted;

    public WeatherAlertBadge(WeatherAlert alert, WeatherAlertTextMode textMode)
    {
        var (fallback, hasSpecificHazard) = CreateFallbackBadge(alert);
        var imageBox = new Grid { Width = 22, Height = 22 };
        imageBox.Children.Add(fallback);

        var title = new TextBlock
        {
            Text = alert.Type,
            FontSize = 14,
            Margin = new Thickness(4, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(imageBox);
        row.Children.Add(title);

        var pill = new Border { Child = row };
        if (Application.Current.Resources.TryGetValue("WeatherAlertPillStyle", out var style) && style is Style pillStyle)
        {
            pill.Style = pillStyle;
        }

        var showTitle = false;
        AcrylicBrush? pillBrush = null;
        Content = pill;
        SetSpecificIcon(!alert.IsDefaultIcon && hasSpecificHazard);
        AutomationProperties.SetName(this, alert.Title);
        ToolTipService.SetToolTip(this, string.IsNullOrWhiteSpace(alert.Detail) ? alert.Title : $"{alert.Title}\n{alert.Detail}");

        ActualThemeChanged += (_, _) =>
        {
            pillBrush = null;
            if (showTitle)
            {
                pill.Background = pillBrush = WeatherVisuals.AlertPillAcrylicBrush(this);
            }
        };
        Unloaded += (_, _) => CancelFallbackLoad();

        if (alert.Icon is not { Scheme: "https" or "http" } uri || !IsAllowedAlertImageHost(uri.Host))
        {
            return;
        }

        var image = new Image
        {
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform
        };
        imageBox.Children.Add(image);

        image.ImageOpened += (_, _) =>
        {
            fallback.Visibility = Visibility.Collapsed;
            SetSpecificIcon(!alert.IsDefaultIcon);
        };
        image.ImageFailed += (_, _) =>
        {
            if (!_httpFallbackStarted)
            {
                _httpFallbackStarted = true;
                _ = LoadWithHttpFallbackAsync(uri, image, fallback, !alert.IsDefaultIcon, hasSpecificHazard, SetSpecificIcon);
                return;
            }

            fallback.Visibility = Visibility.Visible;
            SetSpecificIcon(!alert.IsDefaultIcon && hasSpecificHazard);
        };
        image.Source = new BitmapImage(uri);

        void SetSpecificIcon(bool hasIcon)
        {
            showTitle = textMode == WeatherAlertTextMode.Always ||
                (textMode == WeatherAlertTextMode.WithoutSpecificIcon && !hasIcon);
            title.Visibility = showTitle ? Visibility.Visible : Visibility.Collapsed;
            if (showTitle)
            {
                pill.Background = pillBrush ??= WeatherVisuals.AlertPillAcrylicBrush(this);
            }
            else
            {
                pill.Background = null;
            }
        }
    }

    private async Task LoadWithHttpFallbackAsync(
        Uri primaryUri,
        Image image,
        UIElement fallback,
        bool remoteHasSpecificIcon,
        bool fallbackHasSpecificHazard,
        Action<bool> setSpecificIcon)
    {
        CancelFallbackLoad();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;
        var dispatcher = Dispatcher;

        var candidates = BuildFallbackUris(primaryUri);
        byte[]? bytes = null;
        foreach (var candidate in candidates)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                using var response = await FallbackHttp.GetAsync(candidate, HttpCompletionOption.ResponseContentRead, token)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var data = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                if (data.Length > 0)
                {
                    bytes = data;
                    break;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch
            {
            }
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (bytes is not { Length: > 0 })
                {
                    fallback.Visibility = Visibility.Visible;
                    setSpecificIcon(remoteHasSpecificIcon && fallbackHasSpecificHazard);
                    return;
                }

                try
                {
                    using var stream = new InMemoryRandomAccessStream();
                    await stream.WriteAsync(bytes.AsBuffer());
                    stream.Seek(0);
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    image.Source = bitmap;
                    fallback.Visibility = Visibility.Collapsed;
                    setSpecificIcon(remoteHasSpecificIcon);
                }
                catch
                {
                    fallback.Visibility = Visibility.Visible;
                    setSpecificIcon(remoteHasSpecificIcon && fallbackHasSpecificHazard);
                }
            });
        }
        catch
        {
        }
    }

    private void CancelFallbackLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private static Uri[] BuildFallbackUris(Uri primaryUri)
    {
        if (string.Equals(primaryUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            var httpUri = new UriBuilder(primaryUri) { Scheme = Uri.UriSchemeHttp, Port = -1 }.Uri;
            return [primaryUri, httpUri];
        }

        return [primaryUri];
    }

    private static bool IsAllowedAlertImageHost(string host) =>
        host.Equals("xiaomi.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".xiaomi.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("mi-img.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".mi-img.com", StringComparison.OrdinalIgnoreCase);

    private static (Border Badge, bool HasSpecificHazard) CreateFallbackBadge(WeatherAlert alert)
    {
        var (background, foreground, border) = ResolveAlertPalette(alert.Level, alert.Title);
        var (symbol, hasSpecificHazard) = ResolveHazardIcon(alert.Type, alert.Title);

        var icon = new ViewFluentIcon
        {
            Icon = symbol,
            IconSize = IconSize.Resizable,
            IconVariant = IconVariant.Filled,
            FontSize = 13,
            Foreground = new SolidColorBrush(foreground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var badge = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(background),
            Child = icon
        };
        if (border is { } borderColor)
        {
            badge.BorderBrush = new SolidColorBrush(borderColor);
            badge.BorderThickness = new Thickness(1);
        }

        return (badge, hasSpecificHazard);
    }

    private static (Color Background, Color Foreground, Color? Border) ResolveAlertPalette(string? level, string title)
    {
        var text = string.IsNullOrWhiteSpace(level) ? title : level;
        if (text.Contains('红', StringComparison.Ordinal))
        {
            return (Color.FromArgb(255, 220, 38, 38), Colors.White, null);
        }

        if (text.Contains('橙', StringComparison.Ordinal))
        {
            return (Color.FromArgb(255, 234, 88, 12), Colors.White, null);
        }

        if (text.Contains('黄', StringComparison.Ordinal))
        {
            return (Color.FromArgb(255, 234, 179, 8), Color.FromArgb(255, 31, 41, 55), null);
        }

        if (text.Contains('蓝', StringComparison.Ordinal))
        {
            return (Color.FromArgb(255, 37, 99, 235), Colors.White, null);
        }

        if (text.Contains('白', StringComparison.Ordinal))
        {
            return (Color.FromArgb(255, 229, 231, 235), Color.FromArgb(255, 31, 41, 55), Color.FromArgb(255, 156, 163, 175));
        }

        return (Color.FromArgb(255, 217, 119, 6), Colors.White, null);
    }

    private static (Icon Symbol, bool HasSpecificHazard) ResolveHazardIcon(string type, string title)
    {
        var text = $"{type} {title}";
        if (text.Contains("台风", StringComparison.Ordinal)) return (Icon.WeatherSqualls, true);
        if (text.Contains("冰雹", StringComparison.Ordinal) || text.Contains('雹', StringComparison.Ordinal)) return (Icon.WeatherHailDay, true);
        if (text.Contains('雷', StringComparison.Ordinal)) return (Icon.WeatherThunderstorm, true);
        if (text.Contains("暴雨", StringComparison.Ordinal) || text.Contains('雨', StringComparison.Ordinal)) return (Icon.WeatherRain, true);
        if (text.Contains('雪', StringComparison.Ordinal)) return (Icon.WeatherSnow, true);
        if (text.Contains("寒潮", StringComparison.Ordinal) || text.Contains("霜", StringComparison.Ordinal) ||
            text.Contains("结冰", StringComparison.Ordinal) || text.Contains("低温", StringComparison.Ordinal) ||
            text.Contains('冻', StringComparison.Ordinal)) return (Icon.WeatherSnowflake, true);
        if (text.Contains('沙', StringComparison.Ordinal) || text.Contains('尘', StringComparison.Ordinal)) return (Icon.WeatherDuststorm, true);
        if (text.Contains('风', StringComparison.Ordinal)) return (Icon.WeatherBlowingSnow, true);
        if (text.Contains("高温", StringComparison.Ordinal) || text.Contains('火', StringComparison.Ordinal) ||
            text.Contains('旱', StringComparison.Ordinal)) return (Icon.Temperature, true);
        if (text.Contains('雾', StringComparison.Ordinal)) return (Icon.WeatherFog, true);
        if (text.Contains('霾', StringComparison.Ordinal) || text.Contains("污染", StringComparison.Ordinal)) return (Icon.WeatherHaze, true);
        return (Icon.Warning, false);
    }
}
