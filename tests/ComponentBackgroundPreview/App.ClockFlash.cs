using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async void LaunchClockFlashPreview()
    {
        try
        {
            var clockTime = new PreviewTimeProvider(new DateTimeOffset(2026, 11, 23, 12, 34, 56, 100, TimeSpan.FromHours(8)));
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<TimeProvider>(clockTime);
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddFeature<TextFeature>();
                services.AddFeature<FontFeature>();
                services.AddFeature<ForegroundFeature>();
                services.AddComponent<ClockComponent>();
            }).Build();

            var root = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 24, 34, 52)),
                Width = 960,
                Height = 360
            };
            Window.Current.Content = root;
            Window.Current.Activate();
            await Task.Delay(150);

            var cases = new (string Name, string FontFamily, FontStyle FontStyle, int CharacterSpacing, bool ShowSeconds, bool Underline, Brush Foreground)[]
            {
                ("YaHei-HHmm", "Microsoft YaHei UI", FontStyle.Normal, 0, false, false, new SolidColorBrush(Colors.White)),
                ("YaHei-HHmmss", "Microsoft YaHei UI", FontStyle.Normal, 0, true, false, new SolidColorBrush(Colors.White)),
                ("Consolas-HHmm", "Consolas", FontStyle.Normal, 0, false, false, new SolidColorBrush(Colors.White)),
                ("SegoeUI-HHmm", "Segoe UI", FontStyle.Normal, 0, false, false, new SolidColorBrush(Colors.White)),
                ("TimesNewRoman-Italic", "Times New Roman", FontStyle.Italic, 0, true, false, new SolidColorBrush(Colors.White)),
                ("Arial-SpacingPos", "Arial", FontStyle.Normal, 120, false, false, new SolidColorBrush(Colors.White)),
                ("Arial-SpacingNeg", "Arial", FontStyle.Normal, -40, true, false, new SolidColorBrush(Colors.White)),
                ("SegoeScript-HHmm", "Segoe Script", FontStyle.Normal, 0, false, false, new SolidColorBrush(Colors.White)),
                ("YaHei-Underline", "Microsoft YaHei UI", FontStyle.Normal, 0, false, true, new SolidColorBrush(Colors.White)),
                ("YaHei-Gradient", "Microsoft YaHei UI", FontStyle.Normal, 0, false, false, new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0.5),
                    EndPoint = new Point(1, 0.5),
                    GradientStops =
                    {
                        new GradientStop { Color = Colors.Red, Offset = 0 },
                        new GradientStop { Color = Colors.Cyan, Offset = 1 }
                    }
                }),
                ("YaHei-Acrylic", "Microsoft YaHei UI", FontStyle.Normal, 0, false, false, new AcrylicBrush
                {
                    BackgroundSource = AcrylicBackgroundSource.Backdrop,
                    TintColor = Colors.White,
                    TintOpacity = 0.8,
                    FallbackColor = Colors.White
                }),
                ("Consolas-Acrylic-HHmmss", "Consolas", FontStyle.Normal, 0, true, false, new AcrylicBrush
                {
                    BackgroundSource = AcrylicBackgroundSource.Backdrop,
                    TintColor = Colors.White,
                    TintOpacity = 0.8,
                    FallbackColor = Colors.White
                })
            };

            foreach (var testCase in cases)
            {
                root.Children.Clear();
                clockTime.SetLocalNow(new DateTimeOffset(2026, 11, 23, 12, 34, 56, 100, TimeSpan.FromHours(8)));

                var clockSettings = new ClockComponentSettings
                {
                    ShowSeconds = testCase.ShowSeconds,
                    FlashSeparator = true
                };
                var componentModel = new ComponentSettings
                {
                    Id = ClockComponent.Id,
                    Settings = clockSettings,
                    Features =
                    [
                        new FeatureInfo
                        {
                            Id = TextFeature.Id,
                            Settings = new TextFeatureSettings
                            {
                                FontSize = 64,
                                FontStyle = testCase.FontStyle,
                                CharacterSpacing = testCase.CharacterSpacing,
                                Underline = testCase.Underline
                            }
                        },
                        new FeatureInfo
                        {
                            Id = FontFeature.Id,
                            Settings = new FontFeatureSettings
                            {
                                FontFamilyName = testCase.FontFamily,
                                FontWeight = 700
                            }
                        },
                        new FeatureInfo
                        {
                            Id = "omls.features.appearance.foreground",
                            Settings = new ForegroundFeatureSettings
                            {
                                Foreground = testCase.Foreground
                            }
                        }
                    ]
                };

                var presenter = new ComponentPresenter
                {
                    Settings = componentModel,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                root.Children.Add(presenter);
                await WaitClockLayoutAsync(root, 250);

                var clock = (ClockComponent)presenter.Content;
                var visibleText = string.Concat(Descendants(clock).OfType<TextBlock>()
                    .Where(tb => tb.Visibility == Visibility.Visible)
                    .Select(tb => tb.Text));
                var expectedText = testCase.ShowSeconds ? "12:34:56" : "12:34";
                Check(visibleText == expectedText, $"{testCase.Name}: text='{visibleText}' expected='{expectedText}'");

                if (testCase.Underline)
                {
                    var allUnderlined = Descendants(clock).OfType<TextBlock>()
                        .Where(tb => tb.Visibility == Visibility.Visible)
                        .All(tb => tb.TextDecorations == TextDecorations.Underline);
                    Check(allUnderlined, $"{testCase.Name}: all visible clock parts receive TextDecorations");
                }

                // Visible phase (ms = 100)
                clockSettings.FlashSeparator = false;
                await WaitClockLayoutAsync(root, 120);
                var visibleWidth = clock.ActualWidth;
                var visibleHeight = clock.ActualHeight;
                var visibleShot = await CapturePixelsAsync(clock);

                // Hidden phase (ms = 600, FlashSeparator = true)
                clockTime.SetLocalNow(new DateTimeOffset(2026, 11, 23, 12, 34, 56, 600, TimeSpan.FromHours(8)));
                clockSettings.FlashSeparator = true;
                await WaitClockLayoutAsync(root, 120);
                var hiddenWidth = clock.ActualWidth;
                var hiddenHeight = clock.ActualHeight;
                var hiddenShot = await CapturePixelsAsync(clock);

                // Restored visible phase (ms = 100)
                clockTime.SetLocalNow(new DateTimeOffset(2026, 11, 23, 12, 34, 56, 100, TimeSpan.FromHours(8)));
                clockSettings.FlashSeparator = false;
                await WaitClockLayoutAsync(root, 120);
                var restoredWidth = clock.ActualWidth;
                var restoredShot = await CapturePixelsAsync(clock);

                var visiblePainted = CountAlphaPixels(visibleShot.Pixels);
                var hiddenPainted = CountAlphaPixels(hiddenShot.Pixels);
                var restoredPainted = CountAlphaPixels(restoredShot.Pixels);
                var visibleBounds = HorizontalPaintedBounds(visibleShot);
                var hiddenBounds = HorizontalPaintedBounds(hiddenShot);
                var hiddenOutsideVisible = CountPixelsPaintedOnlyInFirst(hiddenShot, visibleShot);

                Check(visibleWidth == hiddenWidth && visibleHeight == hiddenHeight && visibleWidth == restoredWidth,
                    $"{testCase.Name}: size stable visible={visibleWidth:0.###}x{visibleHeight:0.###} hidden={hiddenWidth:0.###}x{hiddenHeight:0.###} restored={restoredWidth:0.###}");
                Check(visiblePainted > hiddenPainted && hiddenPainted > 0,
                    $"{testCase.Name}: separator hid cleanly visiblePixels={visiblePainted} hiddenPixels={hiddenPainted} restoredPixels={restoredPainted}");
                Check(restoredPainted == visiblePainted,
                    $"{testCase.Name}: separator restored cleanly visiblePixels={visiblePainted} restoredPixels={restoredPainted}");
                Check(visibleBounds == hiddenBounds && hiddenOutsideVisible == 0,
                    $"{testCase.Name}: zero digit shift visibleBounds={visibleBounds} hiddenBounds={hiddenBounds} shiftedPixels={hiddenOutsideVisible}");
            }

            // Verify TextAlignment & LineHeight propagation across all parts on a fixed-width clock
            root.Children.Clear();
            clockTime.SetLocalNow(new DateTimeOffset(2026, 11, 23, 12, 34, 56, 100, TimeSpan.FromHours(8)));
            var alignTextSettings = new TextFeatureSettings
            {
                FontSize = 48,
                LineHeight = 96,
                TextAlignment = TextAlignment.Right
            };
            var alignModel = new ComponentSettings
            {
                Id = ClockComponent.Id,
                Settings = new ClockComponentSettings { ShowSeconds = true },
                Features =
                [
                    new FeatureInfo { Id = TextFeature.Id, Settings = alignTextSettings },
                    new FeatureInfo { Id = "omls.features.appearance.foreground", Settings = new ForegroundFeatureSettings { Foreground = new SolidColorBrush(Colors.White) } }
                ]
            };
            var alignPresenter = new ComponentPresenter { Settings = alignModel, Width = 500 };
            root.Children.Add(alignPresenter);
            await WaitClockLayoutAsync(root, 150);
            var alignClock = (ClockComponent)alignPresenter.Content;
            var partsPanel = Descendants(alignClock).OfType<StackPanel>().Single();
            var allPartsHeight = Descendants(alignClock).OfType<TextBlock>().All(tb => tb.LineHeight == 96);
            Check(allPartsHeight && partsPanel.HorizontalAlignment == HorizontalAlignment.Right,
                $"Alignment/LineHeight: allPartsHeight={allPartsHeight} panelAlignment={partsPanel.HorizontalAlignment}");
            alignTextSettings.TextAlignment = TextAlignment.Center;
            await WaitClockLayoutAsync(root, 100);
            Check(partsPanel.HorizontalAlignment == HorizontalAlignment.Center,
                $"Alignment update to Center: panelAlignment={partsPanel.HorizontalAlignment}");
        }
        catch (Exception exception)
        {
            _report.Add("FAIL: " + exception);
        }

        var reportFile = await ApplicationData.Current.LocalFolder.CreateFileAsync("clock-flash-report.txt", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(reportFile, string.Join(Environment.NewLine, _report));
        Application.Current.Exit();
    }

    private static async Task WaitClockLayoutAsync(FrameworkElement root, int delayMs)
    {
        root.UpdateLayout();
        await Task.Delay(delayMs);
        root.UpdateLayout();
    }

    private static async Task<(byte[] Pixels, int Width, int Height)> CapturePixelsAsync(FrameworkElement element)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        return ((await bitmap.GetPixelsAsync()).ToArray(), bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private static int CountAlphaPixels(byte[] pixels)
    {
        var count = 0;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 0)
            {
                count++;
            }
        }

        return count;
    }

    private static (int MinX, int MaxX) HorizontalPaintedBounds((byte[] Pixels, int Width, int Height) shot)
    {
        var minX = int.MaxValue;
        var maxX = -1;
        for (var y = 0; y < shot.Height; y++)
        {
            for (var x = 0; x < shot.Width; x++)
            {
                var alpha = shot.Pixels[(y * shot.Width + x) * 4 + 3];
                if (alpha > 0)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                }
            }
        }

        return (minX, maxX);
    }

    private static int CountPixelsPaintedOnlyInFirst(
        (byte[] Pixels, int Width, int Height) first,
        (byte[] Pixels, int Width, int Height) second)
    {
        if (first.Width != second.Width || first.Height != second.Height)
        {
            return int.MaxValue;
        }

        var diff = 0;
        for (var i = 3; i < first.Pixels.Length; i += 4)
        {
            if (first.Pixels[i] > 0 && second.Pixels[i] == 0)
            {
                diff++;
            }
        }

        return diff;
    }

    private sealed class PreviewTimeProvider(DateTimeOffset localNow) : TimeProvider
    {
        private DateTimeOffset _localNow = localNow;
        private readonly TimeZoneInfo _zone = TimeZoneInfo.CreateCustomTimeZone("Preview+8", TimeSpan.FromHours(8), "Preview+8", "Preview+8");

        public override TimeZoneInfo LocalTimeZone => _zone;
        public override DateTimeOffset GetUtcNow() => _localNow.ToUniversalTime();
        public void SetLocalNow(DateTimeOffset localNow) => _localNow = localNow;
    }
}
