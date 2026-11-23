using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Controls.Rules;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Models.Features.Visibility;
using OhMyLockScreen.Models.Rules;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Services.Features.Layout;
using OhMyLockScreen.Services.Features.Visibility;
using OhMyLockScreen.Services.Rules;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Services;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Registry;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Models.Weather;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async void LaunchShadowPreview()
    {
        var local = ApplicationData.Current.LocalFolder.Path;
        var report = Path.Combine(local, "shadow-report.txt");
        File.WriteAllText(report, "RUNNING");
        _report.Clear();
        try
        {
            var playing = true;
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IRulesetService, RulesetService>();
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddComponent<TextComponent>();
                services.AddFeature<ShadowFeature, ShadowSettingsControl>();
                services.AddFeature<BorderFeature, BorderSettingsControl>();
                services.AddFeature<BackgroundFeature, BackgroundSettingsControl>();
                services.AddFeature<ForegroundFeature, ForegroundSettingsControl>();
                services.AddFeature<TextFeature, TextFeatureSettingsControl>();
                services.AddFeature<LayoutFeature, LayoutSettingsControl>();
                services.AddFeature<TransformFeature>();
                services.AddFeature<HideByRulesFeature, HideByRulesSettingsControl>();
                services.AddWeatherIconTemplate(WeatherIconIds.Lucide, "Lucide", "LucideWeatherIconTemplate");
                services.AddSingleton<IWeatherService>(_ => new WeatherService(
                    new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(local, "ShadowWeatherSettings.json")),
                    new HttpClient(), TimeProvider.System, NullLogger<WeatherService>.Instance));
                services.AddRule<CurrentWeatherRuleSettings, CurrentWeatherRuleControl>(BuiltinRules.WeatherId, "当前天气是");
                services.AddRule<RainTimeRuleSettings, RainTimeRuleControl>(BuiltinRules.RainId, "距离降水开始/结束还剩", BuiltinRules.RainIcon);
                services.AddRule(BuiltinRules.PlayingId, "有媒体正在播放", "\uE768", _ => playing);
                services.AddRule<MediaFieldRuleSettings, MediaFieldRuleControl>(BuiltinRules.FieldId, "媒体字段是", "\uE8D6", _ => false);
            }).Build();
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(SceneSettings).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(App).Assembly);

            var windowRoot = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 24, 34, 52)),
                Width = 960,
                Height = 640
            };
            var captureRoot = new Grid
            {
                Width = 360,
                Height = 220,
                Padding = new Thickness(64),
                Background = new SolidColorBrush(Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            windowRoot.Children.Add(captureRoot);
            Window.Current.Content = windowRoot;
            Window.Current.Activate();
            await Task.Delay(250);

            // 1. 纯文字组件轮廓阴影：颜色、偏移、空白区域透明与实时更新/清除
            var textShadow = new ShadowFeatureSettings
            {
                Color = Color.FromArgb(255, 255, 0, 0),
                Opacity = 1,
                BlurRadius = 12,
                OffsetX = 20,
                OffsetY = 20
            };
            var textShadowFeature = new FeatureInfo { Id = ShadowFeature.Id, Settings = textShadow };
            var textModel = new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = new TextComponentSettings { Text = "H" },
                Features =
                [
                    new FeatureInfo { Id = "omls.features.appearance.text", Settings = new TextFeatureSettings { FontSize = 56, TextAlignment = TextAlignment.Left } },
                    new FeatureInfo { Id = "omls.features.appearance.foreground", Settings = new ForegroundFeatureSettings { Foreground = new SolidColorBrush(Colors.White) } },
                    new FeatureInfo { Id = "omls.features.layout.layout", Settings = new LayoutFeatureSettings { Width = 160, Height = 64, IsAutoWidth = false, IsAutoHeight = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top } },
                    textShadowFeature
                ]
            };
            var textPresenter = new ComponentPresenter { Settings = textModel };
            captureRoot.Children.Add(textPresenter);
            await Task.Delay(250);

            var textImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "shadow-text-red.png");
            var redPixels = CountPixels(textImage, p => p.A > 20 && p.R >= p.A / 2 && p.G < 20 && p.B < 20);
            var redBelow = CountPixelsInDipRect(textImage, captureRoot, 64, 128, 224, 176, p => p.A > 20 && p.R >= p.A / 2 && p.G < 20 && p.B < 20);
            var emptyRightBoxAlpha = SampleDip(textImage, captureRoot, 200, 96).A;
            var farCornerAlpha = SampleDip(textImage, captureRoot, 4, 4).A;
            Check(redPixels > 200 && redBelow > 20 && emptyRightBoxAlpha == 0 && farCornerAlpha == 0,
                $"Text contour shadow renders offset red halo (total={redPixels}, below={redBelow}, rightEmptyAlpha={emptyRightBoxAlpha}, cornerAlpha={farCornerAlpha})");

            textShadow.Color = Color.FromArgb(255, 0, 255, 0);
            textShadow.OffsetX = -20;
            textShadow.OffsetY = -28;
            await Task.Delay(200);
            var greenTextImage = await CaptureComposedPixelsAsync(captureRoot);
            var remainingRed = CountPixels(greenTextImage, p => p.A > 20 && p.R >= p.A / 2 && p.G < 20 && p.B < 20);
            var greenTopLeft = CountPixelsInDipRect(greenTextImage, captureRoot, 24, 24, 160, 64, p => p.A > 20 && p.G >= p.A / 2 && p.R < 20 && p.B < 20);
            Check(remainingRed == 0 && greenTopLeft > 20,
                $"Updating color and offset updates live shadow pixels (remainingRed={remainingRed}, greenTopLeft={greenTopLeft})");

            textShadow.Opacity = 0;
            await Task.Delay(150);
            var zeroOpacityImage = await CaptureComposedPixelsAsync(captureRoot);
            Check(CountPixels(zeroOpacityImage, p => p.A > 20 && p.G >= p.A / 2 && p.R < 20 && p.B < 20) == 0
                && SampleDip(zeroOpacityImage, captureRoot, 44, 44).A == 0,
                "Setting Opacity=0 hides shadow completely");

            textShadow.Opacity = 1;
            await Task.Delay(150);
            textModel.Features.Remove(textShadowFeature);
            await Task.Delay(150);
            var removedTextImage = await CaptureComposedPixelsAsync(captureRoot);
            Check(CountPixels(removedTextImage, p => p.A > 20 && p.G >= p.A / 2 && p.R < 20 && p.B < 20) == 0,
                "Removing ShadowFeature clears text shadow pixels");

            captureRoot.Children.Clear();

            // 2. 圆角边框卡片 + 动态调整大小
            var cardLayout = new LayoutFeatureSettings
            {
                Width = 140,
                Height = 60,
                IsAutoWidth = false,
                IsAutoHeight = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            var cardModel = new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = new TextComponentSettings { Text = "" },
                Features =
                [
                    new FeatureInfo { Id = "omls.features.layout.layout", Settings = cardLayout },
                    new FeatureInfo { Id = "omls.features.appearance.background", Settings = new BackgroundFeatureSettings { Background = new SolidColorBrush(Colors.White) } },
                    new FeatureInfo { Id = "omls.features.appearance.border", Settings = new BorderFeatureSettings { BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(24) } },
                    new FeatureInfo
                    {
                        Id = ShadowFeature.Id,
                        Settings = new ShadowFeatureSettings
                        {
                            Color = Color.FromArgb(255, 0, 128, 255),
                            Opacity = 1,
                            BlurRadius = 10,
                            OffsetX = 0,
                            OffsetY = 12
                        }
                    }
                ]
            };
            var cardPresenter = new ComponentPresenter { Settings = cardModel };
            captureRoot.Children.Add(cardPresenter);
            await Task.Delay(250);

            var cardImage = await CaptureComposedPixelsAsync(captureRoot);
            var cornerPixel = SampleDip(cardImage, captureRoot, 66, 66);
            var belowCardPixel = SampleDip(cardImage, captureRoot, 134, 132);
            Check(!(cornerPixel.A == 255 && cornerPixel.R == 255 && cornerPixel.G == 255 && cornerPixel.B == 255)
                && belowCardPixel.A > 40 && belowCardPixel.B > 120 && belowCardPixel.R < 40,
                $"Rounded card casts blue shadow outside rounded border (corner={cornerPixel}, below={belowCardPixel})");

            cardLayout.Width = 200;
            cardLayout.Height = 80;
            await Task.Delay(200);
            var resizedCardImage = await CaptureComposedPixelsAsync(captureRoot);
            var resizedBelow = SampleDip(resizedCardImage, captureRoot, 240, 152);
            Check(resizedBelow.A > 40 && resizedBelow.B > 120 && resizedBelow.R < 40,
                $"Resizing component expands shadow without clipping (resizedBelow={resizedBelow})");

            captureRoot.Children.Clear();

            // 3. 亚克力背景 + 编辑器隐藏斜条纹遮罩共存
            var ruleSettings = new HideByRulesSettings();
            ruleSettings.Ruleset.Groups[0].Rules[0].Id = BuiltinRules.PlayingId;
            var acrylicRuleModel = new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = new TextComponentSettings { Text = "" },
                Features =
                [
                    new FeatureInfo
                    {
                        Id = "omls.features.layout.layout",
                        Settings = new LayoutFeatureSettings
                        {
                            Width = 160,
                            Height = 64,
                            IsAutoWidth = false,
                            IsAutoHeight = false,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top
                        }
                    },
                    new FeatureInfo
                    {
                        Id = "omls.features.appearance.background",
                        Settings = new BackgroundFeatureSettings
                        {
                            Background = new AcrylicBrush
                            {
                                BackgroundSource = AcrylicBackgroundSource.Backdrop,
                                TintColor = Color.FromArgb(255, 30, 40, 80),
                                TintOpacity = 0.6,
                                FallbackColor = Color.FromArgb(255, 30, 40, 80)
                            }
                        }
                    },
                    new FeatureInfo
                    {
                        Id = "omls.features.appearance.border",
                        Settings = new BorderFeatureSettings
                        {
                            BorderThickness = new Thickness(4),
                            BorderBrush = new SolidColorBrush(Colors.White)
                        }
                    },
                    new FeatureInfo
                    {
                        Id = ShadowFeature.Id,
                        Settings = new ShadowFeatureSettings
                        {
                            Color = Color.FromArgb(255, 255, 0, 255),
                            Opacity = 1,
                            BlurRadius = 8,
                            OffsetX = 0,
                            OffsetY = 14
                        }
                    },
                    new FeatureInfo { Id = "omls.features.visibility.hideByRules", Settings = ruleSettings }
                ]
            };
            var acrylicPresenter = new ComponentPresenter { IsEditing = true, Settings = acrylicRuleModel };
            captureRoot.Children.Add(acrylicPresenter);
            await Task.Delay(250);

            var acrylicImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "shadow-acrylic-stripes.png");
            var stripeAlphas = Enumerable.Range(68, 80).Select(x => SampleDip(acrylicImage, captureRoot, x, 66).A).ToArray();
            var magentaBelow = CountPixelsInDipRect(acrylicImage, captureRoot, 64, 130, 224, 152, p => p.A > 30 && p.R > 100 && p.B > 100 && p.G < 40);
            Check(stripeAlphas.Any(a => Math.Abs(a - 0x50) < 6) && stripeAlphas.Any(a => Math.Abs(a - 0xAF) < 6) && magentaBelow > 20,
                $"Acrylic + editor stripes preserve diagonal mask and magenta shadow (minA={stripeAlphas.Min()}, maxA={stripeAlphas.Max()}, magentaBelow={magentaBelow})");

            captureRoot.Children.Clear();

            // 4. 图层整体轮廓阴影 + TransformFeature 共存
            captureRoot.Width = 420;
            captureRoot.Height = 260;
            await Task.Delay(100);

            var layerShadowFeature = new FeatureInfo
            {
                Id = ShadowFeature.Id,
                Settings = new ShadowFeatureSettings
                {
                    Color = Color.FromArgb(255, 0, 255, 255),
                    Opacity = 1,
                    BlurRadius = 8,
                    OffsetX = 0,
                    OffsetY = 16
                }
            };
            var layerModel = new LayerSettings
            {
                Kind = LayoutKind.StackPanel,
                Orientation = Orientation.Horizontal,
                Spacing = 40,
                Features =
                [
                    layerShadowFeature,
                    new FeatureInfo { Id = "omls.features.layout.transform", Settings = new TransformFeatureSettings { TranslateX = 24 } }
                ],
                Children =
                [
                    MakeWhiteBox(60, 60),
                    MakeWhiteBox(60, 60)
                ]
            };
            var layerPresenter = new LayerPresenter
            {
                Layer = layerModel,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            captureRoot.Children.Add(layerPresenter);
            await Task.Delay(300);

            var layerImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "shadow-layer-cyan.png");
            var belowFirst = SampleDip(layerImage, captureRoot, 118, 136);
            var belowSecond = SampleDip(layerImage, captureRoot, 218, 136);
            var gapCenter = SampleDip(layerImage, captureRoot, 168, 94);
            Check(belowFirst.A > 40 && belowFirst.G > 120 && belowFirst.B > 120 && belowFirst.R < 40
                && belowSecond.A > 40 && belowSecond.G > 120 && belowSecond.B > 120 && belowSecond.R < 40
                && gapCenter.A == 0,
                $"LayerPresenter casts unified contour shadow preserving gap transparency (below1={belowFirst}, below2={belowSecond}, gap={gapCenter})");

            layerModel.Features.Remove(layerShadowFeature);
            await Task.Delay(200);
            var clearedLayerImage = await CaptureComposedPixelsAsync(captureRoot);
            var remainingCyan = CountPixels(clearedLayerImage, p => p.A > 20 && p.G > 120 && p.B > 120 && p.R < 40);
            Check(remainingCyan == 0 && SampleDip(clearedLayerImage, captureRoot, 118, 136).A == 0,
                $"Removing layer ShadowFeature clears all shadow pixels (remainingCyan={remainingCyan})");

            // 5. ShadowSettingsControl UI 冒烟检查
            var editorFeatureInfo = new FeatureInfo { Id = ShadowFeature.Id };
            var control = (ShadowSettingsControl)IAppHost.GetRequiredService<IFeatureService>().CreateSettingsControl(editorFeatureInfo)!;
            windowRoot.Children.Add(control);
            control.HorizontalAlignment = HorizontalAlignment.Right;
            control.Width = 320;
            await Task.Delay(150);

            var glowButton = (ToggleButton)control.FindName("GlowPresetButton");
            ((IToggleProvider)new ToggleButtonAutomationPeer(glowButton).GetPattern(PatternInterface.Toggle)).Toggle();
            await Task.Delay(80);
            var editedShadow = editorFeatureInfo.GetSettings<ShadowFeatureSettings>();
            var swatchBrush = (SolidColorBrush)control.FindName("ColorSwatchBrush");
            Check(editedShadow.Color == Colors.White && editedShadow.Opacity == 0.6 && editedShadow.BlurRadius == 24
                && editedShadow.OffsetX == 0 && editedShadow.OffsetY == 0 && swatchBrush.Color == Colors.White,
                "ShadowSettingsControl glow preset updates settings and color swatch");

            var resetButton = (Button)control.FindName("ResetButton");
            ((IInvokeProvider)new ButtonAutomationPeer(resetButton).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(80);
            Check(editedShadow.Color == Colors.Black && editedShadow.Opacity == 0.5 && editedShadow.BlurRadius == 16
                && editedShadow.OffsetX == 0 && editedShadow.OffsetY == 4 && swatchBrush.Color == Colors.Black,
                "ShadowSettingsControl reset button restores defaults and swatch");

            File.WriteAllLines(report, _report);
        }
        catch (Exception exception)
        {
            File.AppendAllText(report, "\nFAIL: " + exception);
        }
        finally
        {
            Exit();
        }
    }

    private static ComponentSettings MakeWhiteBox(double width, double height) => new()
    {
        Id = TextComponent.Id,
        Settings = new TextComponentSettings { Text = "" },
        Features =
        [
            new FeatureInfo
            {
                Id = "omls.features.layout.layout",
                Settings = new LayoutFeatureSettings
                {
                    Width = width,
                    Height = height,
                    IsAutoWidth = false,
                    IsAutoHeight = false
                }
            },
            new FeatureInfo
            {
                Id = "omls.features.appearance.background",
                Settings = new BackgroundFeatureSettings { Background = new SolidColorBrush(Colors.White) }
            }
        ]
    };

    private static Color SampleDip((byte[] Pixels, int Width, int Height) image, FrameworkElement element, double x, double y)
    {
        var scaleX = image.Width / element.ActualWidth;
        var scaleY = image.Height / element.ActualHeight;
        var px = Math.Clamp((int)Math.Round(x * scaleX), 0, image.Width - 1);
        var py = Math.Clamp((int)Math.Round(y * scaleY), 0, image.Height - 1);
        return PixelAt(image, px, py);
    }

    private static int CountPixels((byte[] Pixels, int Width, int Height) image, Func<Color, bool> predicate)
    {
        var count = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (predicate(PixelAt(image, x, y)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountPixelsInDipRect(
        (byte[] Pixels, int Width, int Height) image,
        FrameworkElement element,
        double left,
        double top,
        double right,
        double bottom,
        Func<Color, bool> predicate)
    {
        var scaleX = image.Width / element.ActualWidth;
        var scaleY = image.Height / element.ActualHeight;
        var minX = Math.Clamp((int)Math.Floor(left * scaleX), 0, image.Width - 1);
        var minY = Math.Clamp((int)Math.Floor(top * scaleY), 0, image.Height - 1);
        var maxX = Math.Clamp((int)Math.Ceiling(right * scaleX), 0, image.Width);
        var maxY = Math.Clamp((int)Math.Ceiling(bottom * scaleY), 0, image.Height);

        var count = 0;
        for (var y = minY; y < maxY; y++)
        {
            for (var x = minX; x < maxX; x++)
            {
                if (predicate(PixelAt(image, x, y)))
                {
                    count++;
                }
            }
        }

        return count;
    }
}
