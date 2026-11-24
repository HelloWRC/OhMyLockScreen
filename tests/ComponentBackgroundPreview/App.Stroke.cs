using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
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
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Registry;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Models.Weather;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using RadialGradientBrush = Microsoft.UI.Xaml.Media.RadialGradientBrush;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async void LaunchStrokePreview()
    {
        var local = ApplicationData.Current.LocalFolder.Path;
        var report = Path.Combine(local, "stroke-report.txt");
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
                services.AddFeature<StrokeFeature, StrokeSettingsControl>();
                services.AddFeature<ShadowFeature, ShadowSettingsControl>();
                services.AddFeature<BorderFeature, BorderSettingsControl>();
                services.AddFeature<BackgroundFeature, BackgroundSettingsControl>();
                services.AddFeature<ForegroundFeature, ForegroundSettingsControl>();
                services.AddFeature<TextFeature, TextFeatureSettingsControl>();
                services.AddFeature<LayoutFeature, LayoutSettingsControl>();
                services.AddFeature<TransformFeature>();
                services.AddFeature<ParallaxFeature>();
                services.AddFeature<HideByRulesFeature, HideByRulesSettingsControl>();
                services.AddWeatherIconTemplate(WeatherIconIds.Lucide, "Lucide", "LucideWeatherIconTemplate");
                services.AddSingleton<IWeatherService>(_ => new WeatherService(
                    new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(local, "StrokeWeatherSettings.json")),
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
            var rasterScale = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362)
                ? captureRoot.XamlRoot?.RasterizationScale ?? 1.0
                : 1.0;
            Check(rasterScale > 0, $"RasterizationScale telemetry (RasterizationScale={rasterScale})");

            // 1. 外部 / 中心 / 内部 描边（实心与镂空）
            var boxLayout = new LayoutFeatureSettings
            {
                Width = 120,
                Height = 60,
                IsAutoWidth = false,
                IsAutoHeight = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            var boxStroke = new StrokeFeatureSettings
            {
                StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)),
                Thickness = 8,
                IsHollow = false,
                Position = StrokePosition.Outside
            };
            var boxStrokeFeature = new FeatureInfo { Id = StrokeFeature.Id, Settings = boxStroke };
            var boxModel = new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = new TextComponentSettings { Text = "" },
                Features =
                [
                    new FeatureInfo { Id = "omls.features.layout.layout", Settings = boxLayout },
                    new FeatureInfo { Id = "omls.features.appearance.background", Settings = new BackgroundFeatureSettings { Background = new SolidColorBrush(Colors.White) } },
                    new FeatureInfo { Id = "omls.features.appearance.border", Settings = new BorderFeatureSettings { BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(12) } },
                    boxStrokeFeature
                ]
            };
            var boxPresenter = new ComponentPresenter { Settings = boxModel };
            captureRoot.Children.Add(boxPresenter);
            await Task.Delay(250);

            var outsideSolid = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-outside-solid.png");
            var outsideLeft = SampleDip(outsideSolid, captureRoot, 60, 94);
            var outsideRight = SampleDip(outsideSolid, captureRoot, 188, 94);
            var outsideCenter = SampleDip(outsideSolid, captureRoot, 124, 94);
            var farCorner = SampleDip(outsideSolid, captureRoot, 40, 40);
            Check(IsRed(outsideLeft) && IsRed(outsideRight) && IsWhite(outsideCenter) && farCorner.A == 0,
                $"Outside solid stroke expands red border outside rounded white box (left={outsideLeft}, right={outsideRight}, center={outsideCenter}, far={farCorner})");

            boxStroke.IsHollow = true;
            await Task.Delay(200);
            var outsideHollow = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-outside-hollow.png");
            var outsideHollowLeft = SampleDip(outsideHollow, captureRoot, 60, 94);
            var outsideHollowCenter = SampleDip(outsideHollow, captureRoot, 124, 94);
            Check(IsRed(outsideHollowLeft) && outsideHollowCenter.A == 0,
                $"Outside hollow stroke preserves outer ring and clears interior (left={outsideHollowLeft}, center={outsideHollowCenter})");

            boxStroke.Position = StrokePosition.Center;
            boxStroke.Thickness = 10;
            boxStroke.IsHollow = false;
            await Task.Delay(200);
            var centerSolid = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-center-solid.png");
            var centerOuter = SampleDip(centerSolid, captureRoot, 61, 94);
            var centerInner = SampleDip(centerSolid, captureRoot, 66, 94);
            var centerBeyond = SampleDip(centerSolid, captureRoot, 54, 94);
            var centerCore = SampleDip(centerSolid, captureRoot, 124, 94);
            Check(IsRed(centerOuter) && IsRed(centerInner) && centerBeyond.A == 0 && IsWhite(centerCore),
                $"Center solid stroke straddles border bounds (outer={centerOuter}, inner={centerInner}, beyond={centerBeyond}, core={centerCore})");

            boxStroke.IsHollow = true;
            await Task.Delay(200);
            var centerHollow = await CaptureComposedPixelsAsync(captureRoot);
            Check(IsRed(SampleDip(centerHollow, captureRoot, 61, 94))
                && IsRed(SampleDip(centerHollow, captureRoot, 66, 94))
                && SampleDip(centerHollow, captureRoot, 124, 94).A == 0,
                "Center hollow stroke keeps straddling band and clears interior");

            boxStroke.Position = StrokePosition.Inside;
            boxStroke.Thickness = 10;
            boxStroke.IsHollow = false;
            boxStroke.StrokeBrush = new SolidColorBrush(Colors.Black);
            await Task.Delay(200);
            var insideSolid = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-inside-solid.png");
            var insideOutsidePixel = SampleDip(insideSolid, captureRoot, 60, 94);
            var insideRingPixel = SampleDip(insideSolid, captureRoot, 68, 94);
            var insideCorePixel = SampleDip(insideSolid, captureRoot, 124, 94);
            Check(insideOutsidePixel.A == 0 && IsBlack(insideRingPixel) && IsWhite(insideCorePixel),
                $"Inside solid black stroke stays strictly inside bounds (outside={insideOutsidePixel}, ring={insideRingPixel}, core={insideCorePixel})");

            boxStroke.IsHollow = true;
            boxStroke.StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
            await Task.Delay(200);
            var insideHollow = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-inside-hollow.png");
            Check(SampleDip(insideHollow, captureRoot, 60, 94).A == 0
                && IsRed(SampleDip(insideHollow, captureRoot, 68, 94))
                && SampleDip(insideHollow, captureRoot, 124, 94).A == 0,
                "Inside hollow stroke leaves outer region and deep interior transparent");

            // 2. 纯文字组件（外描边 + 黑内描边无白光晕 + 动态调整大小 + 归零/置空）
            captureRoot.Children.Clear();
            var textStroke = new StrokeFeatureSettings
            {
                StrokeBrush = new SolidColorBrush(Colors.Black),
                Thickness = 3,
                IsHollow = false,
                Position = StrokePosition.Inside
            };
            var textModel = new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = new TextComponentSettings { Text = "H" },
                Features =
                [
                    new FeatureInfo { Id = "omls.features.appearance.text", Settings = new TextFeatureSettings { FontSize = 64, TextAlignment = TextAlignment.Left } },
                    new FeatureInfo { Id = "omls.features.appearance.foreground", Settings = new ForegroundFeatureSettings { Foreground = new SolidColorBrush(Colors.White) } },
                    new FeatureInfo { Id = "omls.features.layout.layout", Settings = new LayoutFeatureSettings { Width = 140, Height = 84, IsAutoWidth = false, IsAutoHeight = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top } },
                    new FeatureInfo { Id = StrokeFeature.Id, Settings = textStroke }
                ]
            };
            var textPresenter = new ComponentPresenter { Settings = textModel };
            captureRoot.Children.Add(textPresenter);
            await Task.Delay(250);

            var textInsideBlack = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-text-inside-black.png");
            var blackPixels = CountPixels(textInsideBlack, IsBlack);
            // SourceAtop 模式下白色字形边缘被黑色内描边完全覆盖，不应有高亮度偏白外溢光晕。
            var edgeBrightPixels = CountPixels(textInsideBlack, p => p.A > 20 && p.A < 210 && p.R > 160 && p.G > 160 && p.B > 160);
            Check(blackPixels > 80 && edgeBrightPixels == 0,
                $"White text with black Inside stroke renders crisp dark outline without outer bright halo (black={blackPixels}, edgeBright={edgeBrightPixels})");

            // 切回盒子测试线性渐变、径向渐变、亚克力、动态缩放与清空
            captureRoot.Children.Clear();
            boxStroke.Position = StrokePosition.Outside;
            boxStroke.Thickness = 8;
            boxStroke.IsHollow = true;
            boxStroke.StrokeBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop { Color = Colors.Red, Offset = 0 },
                    new GradientStop { Color = Colors.Blue, Offset = 1 }
                }
            };
            captureRoot.Children.Add(boxPresenter);
            await Task.Delay(250);

            var linearImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-linear-gradient.png");
            var gradLeft = SampleDip(linearImage, captureRoot, 60, 94);
            var gradRight = SampleDip(linearImage, captureRoot, 188, 94);
            Check(gradLeft.A > 200 && gradLeft.R > gradLeft.B + 120
                && gradRight.A > 200 && gradRight.B > gradRight.R + 120,
                $"LinearGradientBrush paints across padded stroke bounds (left={gradLeft}, right={gradRight})");

            boxStroke.StrokeBrush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.6,
                RadiusY = 0.6,
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 0, 255, 0), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(255, 255, 255, 0), Offset = 1 }
                }
            };
            await Task.Delay(200);
            var radialImage = await CaptureComposedPixelsAsync(captureRoot);
            var radialEdge = SampleDip(radialImage, captureRoot, 60, 94);
            Check(radialEdge.A > 200 && radialEdge.G > 180,
                $"RadialGradientBrush paints live stroke outline (edge={radialEdge})");

            boxLayout.Width = 180;
            boxLayout.Height = 80;
            await Task.Delay(200);
            var resizedImage = await CaptureComposedPixelsAsync(captureRoot);
            var resizedRight = SampleDip(resizedImage, captureRoot, 248, 104);
            Check(resizedRight.A > 200 && resizedRight.G > 180,
                $"Resizing component expands stroke bounds without clipping (right={resizedRight})");

            // 亚克力描边：在 captureRoot 后方放置红蓝背景色块以验证实时 Backdrop 穿透
            boxLayout.Width = 120;
            boxLayout.Height = 60;
            var backdropSplit = new Grid
            {
                Width = 248,
                Height = 124,
                Margin = new Thickness(-64, -32, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                }
            };
            var leftBackdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 255, 40, 40)) };
            var rightBackdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 255)) };
            Grid.SetColumn(rightBackdrop, 1);
            backdropSplit.Children.Add(leftBackdrop);
            backdropSplit.Children.Add(rightBackdrop);
            captureRoot.Children.Insert(0, backdropSplit);

            boxStroke.StrokeBrush = new AcrylicBrush
            {
                BackgroundSource = AcrylicBackgroundSource.Backdrop,
                TintColor = Colors.White,
                TintOpacity = 0.25,
                FallbackColor = Color.FromArgb(255, 128, 128, 128)
            };
            await Task.Delay(250);
            var acrylicStrokeImage = await CaptureComposedPixelsAsync(captureRoot);
            var acrylicNativeImage = await RenderElementAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-acrylic-hollow.png");
            var acrylicLeft = SampleDip(acrylicStrokeImage, captureRoot, 60, 94);
            var acrylicRight = SampleDip(acrylicStrokeImage, captureRoot, 188, 94);
            var nativeLeft = SampleDip(acrylicNativeImage, captureRoot, 60, 94);
            var nativeRight = SampleDip(acrylicNativeImage, captureRoot, 188, 94);
            var nativeTop = SampleDip(acrylicNativeImage, captureRoot, 124, 60);
            var nativeBottom = SampleDip(acrylicNativeImage, captureRoot, 124, 128);
            var nativeHollowCenter = SampleDip(acrylicNativeImage, captureRoot, 124, 94);
            var nativeHollowRight = SampleDip(acrylicNativeImage, captureRoot, 140, 94);
            Check(acrylicLeft.R > acrylicLeft.B + 40 && acrylicRight.B > acrylicRight.R + 40
                && nativeLeft.R > 200 && nativeLeft.G > 75 && nativeLeft.B > 75
                && nativeRight.B > 200 && nativeRight.R > 75 && nativeRight.G > 75
                && nativeTop.G > 75 && nativeBottom.G > 75
                && nativeHollowCenter.G < 55 && nativeHollowRight.G < 55,
                $"AcrylicBrush hollow stroke aligns at native DPI without top-left shrink (left={nativeLeft}, right={nativeRight}, top={nativeTop}, bottom={nativeBottom}, center={nativeHollowCenter}, innerRight={nativeHollowRight})");

            boxStroke.IsHollow = false;
            await Task.Delay(200);
            var acrylicSolidImage = await RenderElementAsync(captureRoot);
            var acrylicSolidLeft = SampleDip(acrylicSolidImage, captureRoot, 60, 94);
            var acrylicSolidRight = SampleDip(acrylicSolidImage, captureRoot, 188, 94);
            var acrylicSolidCore = SampleDip(acrylicSolidImage, captureRoot, 124, 94);
            Check(acrylicSolidLeft.R > 200 && acrylicSolidLeft.G > 75 && acrylicSolidLeft.B > 75
                && acrylicSolidRight.B > 200 && acrylicSolidRight.R > 75 && acrylicSolidRight.G > 75
                && IsWhite(acrylicSolidCore),
                $"AcrylicBrush solid stroke preserves full outer tint and opaque interior (left={acrylicSolidLeft}, right={acrylicSolidRight}, core={acrylicSolidCore})");

            boxStroke.IsHollow = true;
            backdropSplit.Width = 280;
            backdropSplit.Height = 160;
            var acrylicTransformFeature = new FeatureInfo
            {
                Id = "omls.features.layout.transform",
                Settings = new TransformFeatureSettings
                {
                    ScaleX = 1.5,
                    ScaleY = 1.5,
                    OriginX = 0,
                    OriginY = 0
                }
            };
            boxModel.Features.Add(acrylicTransformFeature);
            await Task.Delay(250);
            var acrylicScaledImage = await RenderElementAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-acrylic-scaled.png");
            var scaledRight = SampleDip(acrylicScaledImage, captureRoot, 250, 109);
            var scaledCenter = SampleDip(acrylicScaledImage, captureRoot, 154, 109);
            Check(scaledRight.B > 200 && scaledRight.R > 75 && scaledRight.G > 75 && scaledCenter.G < 55,
                $"AcrylicBrush stroke aligns accurately under TransformFeature scale=1.5 (right={scaledRight}, center={scaledCenter})");

            boxModel.Features.Remove(acrylicTransformFeature);
            await Task.Delay(150);
            captureRoot.Children.Remove(backdropSplit);

            // 粗细归零与清空画刷时的镂空表现
            boxStroke.Thickness = 0;
            await Task.Delay(150);
            var zeroThicknessHollow = await CaptureComposedPixelsAsync(captureRoot);
            Check(SampleDip(zeroThicknessHollow, captureRoot, 60, 94).A == 0
                && SampleDip(zeroThicknessHollow, captureRoot, 124, 94).A == 0,
                "Thickness=0 with IsHollow=true hides content and draws no stroke");

            boxStroke.IsHollow = false;
            await Task.Delay(150);
            var zeroThicknessSolid = await CaptureComposedPixelsAsync(captureRoot);
            Check(IsWhite(SampleDip(zeroThicknessSolid, captureRoot, 124, 94))
                && SampleDip(zeroThicknessSolid, captureRoot, 60, 94).A == 0,
                "Thickness=0 with IsHollow=false restores unstroked content");

            boxStroke.Thickness = 8;
            boxStroke.IsHollow = true;
            boxStroke.StrokeBrush = null;
            await Task.Delay(150);
            var nullBrushHollow = await CaptureComposedPixelsAsync(captureRoot);
            Check(SampleDip(nullBrushHollow, captureRoot, 124, 94).A == 0
                && SampleDip(nullBrushHollow, captureRoot, 60, 94).A == 0,
                "Null StrokeBrush with IsHollow=true hides content and draws no stroke");

            // 最大 64 DIP 中心描边压测与边缘锐度检查
            boxStroke.StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
            boxStroke.Thickness = 64;
            boxStroke.Position = StrokePosition.Center;
            boxStroke.IsHollow = false;
            await Task.Delay(250);
            var maxThicknessImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-max-64.png");
            var maxOuterLeft = SampleDip(maxThicknessImage, captureRoot, 40, 94);
            var maxOuterRight = SampleDip(maxThicknessImage, captureRoot, 208, 94);
            var maxBeyondLeft = SampleDip(maxThicknessImage, captureRoot, 24, 94);
            var maxCenterSolid = SampleDip(maxThicknessImage, captureRoot, 124, 94);
            var maxCenterLeftRamp = MeasureHorizontalEdgeRampPx(maxThicknessImage, captureRoot, 94, 24, 40);
            var maxCenterRightRamp = MeasureHorizontalEdgeRampPx(maxThicknessImage, captureRoot, 94, 208, 224);
            Check(IsRed(maxOuterLeft) && IsRed(maxOuterRight) && maxBeyondLeft.A == 0
                && maxCenterSolid.A >= 240 && maxCenterSolid.R >= 240 && maxCenterSolid.G < 15 && maxCenterSolid.B < 15
                && maxCenterLeftRamp <= 3 && maxCenterRightRamp <= 3,
                $"Max 64 DIP Center stroke renders solid interior and crisp edges (center={maxCenterSolid}, leftRamp={maxCenterLeftRamp}px, rightRamp={maxCenterRightRamp}px)");

            // 最大 64 DIP 外部描边压测与边缘锐度检查
            boxStroke.Position = StrokePosition.Outside;
            await Task.Delay(250);
            var maxOutsideImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-max-64-outside.png");
            var maxOutLeft = SampleDip(maxOutsideImage, captureRoot, 12, 94);
            var maxOutRight = SampleDip(maxOutsideImage, captureRoot, 236, 94);
            var maxOutBeyond = SampleDip(maxOutsideImage, captureRoot, 256, 94);
            var maxOutCenter = SampleDip(maxOutsideImage, captureRoot, 124, 94);
            var maxOutsideRightRamp = MeasureHorizontalEdgeRampPx(maxOutsideImage, captureRoot, 94, 236, 256);
            var maxOutsideBottomRamp = MeasureVerticalEdgeRampPx(maxOutsideImage, captureRoot, 124, 178, 198);
            Check(IsRed(maxOutLeft) && IsRed(maxOutRight) && maxOutBeyond.A == 0 && IsWhite(maxOutCenter)
                && maxOutsideRightRamp <= 3 && maxOutsideBottomRamp <= 3,
                $"Max 64 DIP Outside stroke expands with razor-sharp outer edges (rightRamp={maxOutsideRightRamp}px, bottomRamp={maxOutsideBottomRamp}px)");

            // SolidColorBrush 就地修改 Color 与 Opacity 快速路径验证
            var liveSolid = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
            boxStroke.StrokeBrush = liveSolid;
            boxStroke.Thickness = 12;
            boxStroke.Position = StrokePosition.Outside;
            boxStroke.IsHollow = false;
            await Task.Delay(120);
            liveSolid.Color = Color.FromArgb(255, 0, 255, 0);
            await Task.Delay(120);
            var liveGreenImage = await CaptureComposedPixelsAsync(captureRoot);
            var liveGreen = SampleDip(liveGreenImage, captureRoot, 56, 94);
            liveSolid.Opacity = 0.5;
            await Task.Delay(120);
            var liveHalfImage = await CaptureComposedPixelsAsync(captureRoot);
            var liveHalf = SampleDip(liveHalfImage, captureRoot, 56, 94);
            Check(liveGreen.A >= 240 && liveGreen.G >= 240 && liveGreen.R < 20
                && liveHalf.A is >= 95 and <= 160,
                $"SolidColorBrush fast path updates color and opacity in-place (green={liveGreen}, halfOpacity={liveHalf})");

            // 文字大半径外描边锐度检查
            captureRoot.Children.Clear();
            textStroke.Position = StrokePosition.Outside;
            textStroke.Thickness = 24;
            textStroke.StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
            textStroke.IsHollow = false;
            captureRoot.Children.Add(textPresenter);
            await Task.Delay(250);
            var textThickImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-text-thick-outside.png");
            var textThickRedCount = CountPixels(textThickImage, IsRed);
            var textThickWhiteCount = CountPixels(textThickImage, IsWhite);
            var textThickCorner = SampleDip(textThickImage, captureRoot, 16, 16);
            var textThickLeftRamp = MeasureHorizontalEdgeRampPx(textThickImage, captureRoot, 106, 32, 60);
            Check(textThickRedCount > 400 && textThickWhiteCount > 100 && textThickCorner.A == 0 && textThickLeftRamp <= 3,
                $"Text with 24 DIP Outside stroke keeps crisp outer contour and zero background fog (red={textThickRedCount}, white={textThickWhiteCount}, corner={textThickCorner}, leftRamp={textThickLeftRamp}px)");

            // 大组件滑块连续调节响应性能压测（480x160 DIP，Outside/Center 24 步扫频 + 最终渲染捕获）
            captureRoot.Children.Clear();
            captureRoot.Width = 680;
            captureRoot.Height = 360;
            captureRoot.Padding = new Thickness(80);
            boxLayout.Width = 480;
            boxLayout.Height = 160;
            boxStroke.StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
            boxStroke.IsHollow = false;
            boxStroke.Position = StrokePosition.Outside;
            boxStroke.Thickness = 4;
            captureRoot.Children.Add(boxPresenter);
            await Task.Delay(150);

            var sweepStopwatch = Stopwatch.StartNew();
            foreach (var pos in new[] { StrokePosition.Outside, StrokePosition.Center })
            {
                boxStroke.Position = pos;
                for (var step = 1; step <= 12; step++)
                {
                    boxStroke.Thickness = Math.Min(64, step * 5.25);
                    await Task.Yield();
                }
            }
            boxStroke.Position = StrokePosition.Outside;
            boxStroke.Thickness = 64;
            captureRoot.UpdateLayout();
            await Task.Delay(50);
            var sweepImage = await CaptureComposedPixelsAsync(captureRoot);
            sweepStopwatch.Stop();
            var sweepOuterPixel = SampleDip(sweepImage, captureRoot, 24, 160);
            var sweepCenterPixel = SampleDip(sweepImage, captureRoot, 320, 160);
            var sweepElapsedMs = sweepStopwatch.ElapsedMilliseconds;
            Check(sweepElapsedMs < 3000 && IsRed(sweepOuterPixel) && IsWhite(sweepCenterPixel),
                $"Large 480x160 slider sweep completes smoothly (elapsed={sweepElapsedMs}ms, outer={sweepOuterPixel}, center={sweepCenterPixel})");

            // 恢复标准画布尺寸用于后续检查
            captureRoot.Children.Clear();
            captureRoot.Width = 360;
            captureRoot.Height = 220;
            captureRoot.Padding = new Thickness(64);
            boxLayout.Width = 120;
            boxLayout.Height = 60;
            boxStroke.Thickness = 8;
            boxStroke.IsHollow = false;
            captureRoot.Children.Add(boxPresenter);
            await Task.Delay(120);

            boxModel.Features.Remove(boxStrokeFeature);
            await Task.Delay(150);
            var removedStrokeImage = await CaptureComposedPixelsAsync(captureRoot);
            Check(IsWhite(SampleDip(removedStrokeImage, captureRoot, 124, 94))
                && SampleDip(removedStrokeImage, captureRoot, 60, 94).A == 0,
                "Removing StrokeFeature restores unstroked content");

            captureRoot.Children.Clear();

            // 3. 与 ShadowDecorator 和 StripeMaskDecorator 共存
            var hollowStrokeShadowFeature = new FeatureInfo
            {
                Id = StrokeFeature.Id,
                Settings = new StrokeFeatureSettings
                {
                    StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)),
                    Thickness = 8,
                    IsHollow = true,
                    Position = StrokePosition.Outside
                }
            };
            var hollowShadowModel = new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = new TextComponentSettings { Text = "" },
                Features =
                [
                    new FeatureInfo { Id = "omls.features.layout.layout", Settings = boxLayout },
                    new FeatureInfo { Id = "omls.features.appearance.background", Settings = new BackgroundFeatureSettings { Background = new SolidColorBrush(Colors.White) } },
                    hollowStrokeShadowFeature,
                    new FeatureInfo
                    {
                        Id = ShadowFeature.Id,
                        Settings = new ShadowFeatureSettings
                        {
                            Color = Color.FromArgb(255, 0, 128, 255),
                            Opacity = 1,
                            BlurRadius = 4,
                            OffsetX = 0,
                            OffsetY = 20
                        }
                    }
                ]
            };
            var hollowShadowPresenter = new ComponentPresenter { Settings = hollowShadowModel };
            captureRoot.Children.Add(hollowShadowPresenter);
            await Task.Delay(300);

            var hollowShadowImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-hollow-shadow.png");
            var blueShadowBelowRing = CountPixelsInDipRect(hollowShadowImage, captureRoot, 64, 144, 184, 160,
                p => p.A > 40 && p.B > 100 && p.R < 50);
            var hollowShadowCenter = SampleDip(hollowShadowImage, captureRoot, 124, 104);
            Check(blueShadowBelowRing > 20 && hollowShadowCenter.A == 0,
                $"Shadow casts hollow ring contour below stroked outline while center stays transparent (blueBelow={blueShadowBelowRing}, center={hollowShadowCenter})");

            captureRoot.Children.Clear();

            var ruleSettings = new HideByRulesSettings();
            ruleSettings.Ruleset.Groups[0].Rules[0].Id = BuiltinRules.PlayingId;
            var stripeStrokeFeature = new FeatureInfo
            {
                Id = StrokeFeature.Id,
                Settings = new StrokeFeatureSettings
                {
                    StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 0, 255, 0)),
                    Thickness = 8,
                    IsHollow = false,
                    Position = StrokePosition.Outside
                }
            };
            var stripeModel = new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = new TextComponentSettings { Text = "" },
                Features =
                [
                    new FeatureInfo { Id = "omls.features.layout.layout", Settings = boxLayout },
                    new FeatureInfo { Id = "omls.features.appearance.background", Settings = new BackgroundFeatureSettings { Background = new SolidColorBrush(Colors.White) } },
                    new FeatureInfo { Id = "omls.features.appearance.border", Settings = new BorderFeatureSettings { BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Colors.White) } },
                    stripeStrokeFeature,
                    new FeatureInfo
                    {
                        Id = ShadowFeature.Id,
                        Settings = new ShadowFeatureSettings
                        {
                            Color = Color.FromArgb(255, 255, 0, 255),
                            Opacity = 1,
                            BlurRadius = 6,
                            OffsetX = 0,
                            OffsetY = 18
                        }
                    },
                    new FeatureInfo { Id = "omls.features.visibility.hideByRules", Settings = ruleSettings }
                ]
            };
            var stripePresenter = new ComponentPresenter { IsEditing = true, Settings = stripeModel };
            captureRoot.Children.Add(stripePresenter);
            await Task.Delay(300);

            Check(stripePresenter.Decorators.Elements.Count == 4
                && stripePresenter.Decorators.Elements[0] is ShadowDecorator
                && stripePresenter.Decorators.Elements[1] is StripeMaskDecorator
                && stripePresenter.Decorators.Elements[2] is StrokeDecorator
                && stripePresenter.Decorators.Elements[3] is Border,
                $"Decorators preserve [ShadowDecorator, StripeMaskDecorator, StrokeDecorator, Border] order (actual={string.Join(", ", stripePresenter.Decorators.Elements.Select(e => e.GetType().Name))})");

            var stripeImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-stripes-shadow.png");
            var topStrokeAlphas = Enumerable.Range(70, 80).Select(x => SampleDip(stripeImage, captureRoot, x, 59).A).ToArray();
            var topStrokeGreens = Enumerable.Range(70, 80).Select(x => SampleDip(stripeImage, captureRoot, x, 59).G).ToArray();
            var magentaBelow = CountPixelsInDipRect(stripeImage, captureRoot, 64, 134, 184, 158,
                p => p.A > 30 && p.R > 80 && p.B > 80 && p.G < 60);
            Check(topStrokeAlphas.Any(a => Math.Abs(a - 0x50) < 12)
                && topStrokeAlphas.Any(a => Math.Abs(a - 0xAF) < 12)
                && topStrokeGreens.Max() > 60
                && magentaBelow > 20,
                $"StripeMaskDecorator preserves outside green stroke with diagonal stripes and magenta shadow (minA={topStrokeAlphas.Min()}, maxA={topStrokeAlphas.Max()}, maxG={topStrokeGreens.Max()}, magentaBelow={magentaBelow})");

            stripeModel.Features.Remove(stripeStrokeFeature);
            await Task.Delay(200);
            stripeModel.Features.Add(stripeStrokeFeature);
            await Task.Delay(250);
            var restoredStripeImage = await CaptureComposedPixelsAsync(captureRoot);
            var restoredAlphas = Enumerable.Range(70, 80).Select(x => SampleDip(restoredStripeImage, captureRoot, x, 59).A).ToArray();
            Check(restoredAlphas.Any(a => Math.Abs(a - 0x50) < 12) && restoredAlphas.Any(a => Math.Abs(a - 0xAF) < 12),
                "Removing and re-adding StrokeFeature preserves StripeMaskDecorator and outside stroke");

            captureRoot.Children.Clear();

            // 4. LayerPresenter 与 ScenePresenter 描边
            captureRoot.Width = 420;
            captureRoot.Height = 260;
            await Task.Delay(100);

            var layerStrokeFeature = new FeatureInfo
            {
                Id = StrokeFeature.Id,
                Settings = new StrokeFeatureSettings
                {
                    StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 0, 255, 255)),
                    Thickness = 6,
                    IsHollow = false,
                    Position = StrokePosition.Outside
                }
            };
            var layerModel = new LayerSettings
            {
                Kind = LayoutKind.StackPanel,
                Orientation = Orientation.Horizontal,
                Spacing = 40,
                Features = [layerStrokeFeature],
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
            await CaptureAsync(captureRoot, "stroke-layer-cyan.png");
            var layerLeftCyan = SampleDip(layerImage, captureRoot, 60, 94);
            var layerGap = SampleDip(layerImage, captureRoot, 144, 94);
            var layerRightCyan = SampleDip(layerImage, captureRoot, 228, 94);
            Check(IsCyan(layerLeftCyan) && IsCyan(layerRightCyan) && layerGap.A == 0,
                $"LayerPresenter strokes unified multi-child contour while preserving gap transparency (left={layerLeftCyan}, gap={layerGap}, right={layerRightCyan})");

            captureRoot.Children.Clear();
            captureRoot.Width = 360;
            captureRoot.Height = 220;
            await Task.Delay(100);

            var sceneStrokeFeature = new FeatureInfo
            {
                Id = StrokeFeature.Id,
                Settings = new StrokeFeatureSettings
                {
                    StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 0)),
                    Thickness = 6,
                    IsHollow = false,
                    Position = StrokePosition.Outside
                }
            };
            var sceneBox = MakeWhiteBox(100, 40);
            ((LayoutFeatureSettings)sceneBox.Features[0].Settings!).Margin = new Thickness(24, 24, 0, 0);
            var sceneModel = new SceneSettings
            {
                Background = new BackgroundSettings { Mode = BackgroundMode.None },
                Features = [sceneStrokeFeature],
                Layers =
                [
                    new LayerSettings
                    {
                        Kind = LayoutKind.Canvas,
                        Children = [sceneBox]
                    }
                ]
            };
            var scenePresenter = new ScenePresenter
            {
                Scene = sceneModel,
                Width = 200,
                Height = 100,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            captureRoot.Children.Add(scenePresenter);
            await Task.Delay(300);

            var sceneImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "stroke-scene-yellow.png");
            var sceneYellowLeft = SampleDip(sceneImage, captureRoot, 85, 108);
            var sceneYellowTop = SampleDip(sceneImage, captureRoot, 138, 85);
            var sceneYellowRight = SampleDip(sceneImage, captureRoot, 191, 108);
            var sceneYellowBottom = SampleDip(sceneImage, captureRoot, 138, 131);
            Check(IsYellow(sceneYellowLeft) && IsYellow(sceneYellowTop) && IsYellow(sceneYellowRight) && IsYellow(sceneYellowBottom),
                $"ScenePresenter renders yellow outside stroke around scene content (left={sceneYellowLeft}, top={sceneYellowTop}, right={sceneYellowRight}, bottom={sceneYellowBottom})");

            sceneModel.Features.Remove(sceneStrokeFeature);
            await Task.Delay(200);
            var clearedSceneImage = await CaptureComposedPixelsAsync(captureRoot);
            Check(SampleDip(clearedSceneImage, captureRoot, 85, 108).A == 0,
                "Removing ScenePresenter StrokeFeature clears stroke pixels");

            // 5. StrokeSettingsControl UI 冒烟检查
            var editorInfo = new FeatureInfo { Id = StrokeFeature.Id };
            var control = (StrokeSettingsControl)IAppHost.GetRequiredService<IFeatureService>().CreateSettingsControl(editorInfo)!;
            windowRoot.Children.Add(control);
            control.HorizontalAlignment = HorizontalAlignment.Right;
            control.Width = 320;
            await Task.Delay(150);

            var positionCombo = (ComboBox)control.FindName("PositionComboBox");
            positionCombo.SelectedIndex = 0; // Inside
            var hollowCheck = (CheckBox)control.FindName("HollowCheckBox");
            ((IToggleProvider)new CheckBoxAutomationPeer(hollowCheck).GetPattern(PatternInterface.Toggle)).Toggle();
            var thicknessInput = (Microsoft.UI.Xaml.Controls.NumberBox)control.FindName("ThicknessInput");
            thicknessInput.Value = 16;
            await Task.Delay(100);

            var editedStroke = editorInfo.GetSettings<StrokeFeatureSettings>();
            Check(editedStroke.Position == StrokePosition.Inside && editedStroke.IsHollow && editedStroke.Thickness == 16,
                $"StrokeSettingsControl edits synchronize to settings (pos={editedStroke.Position}, hollow={editedStroke.IsHollow}, thickness={editedStroke.Thickness})");

            var resetButton = (Button)control.FindName("ResetButton");
            ((IInvokeProvider)new ButtonAutomationPeer(resetButton).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            Check(editedStroke.Position == StrokePosition.Outside && !editedStroke.IsHollow && editedStroke.Thickness == 2
                && positionCombo.SelectedIndex == 2 && hollowCheck.IsChecked == false,
                $"StrokeSettingsControl reset button restores defaults and UI controls (pos={editedStroke.Position}, hollow={editedStroke.IsHollow}, thickness={editedStroke.Thickness}, combo={positionCombo.SelectedIndex}, check={hollowCheck.IsChecked})");
        }
        catch (Exception exception)
        {
            _report.Add("FAIL: " + exception);
        }
        finally
        {
            File.WriteAllLines(report, _report);
            Exit();
        }
    }

    private static async Task<(byte[] Pixels, int Width, int Height)> RenderElementAsync(FrameworkElement element)
    {
        element.UpdateLayout();
        await Task.Delay(100);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        return ((await bitmap.GetPixelsAsync()).ToArray(), bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private static int MeasureHorizontalEdgeRampPx(
        (byte[] Pixels, int Width, int Height) image,
        FrameworkElement element,
        double dipY,
        double startDipX,
        double endDipX)
    {
        var scaleX = image.Width / element.ActualWidth;
        var scaleY = image.Height / element.ActualHeight;
        var py = Math.Clamp((int)Math.Round(dipY * scaleY), 0, image.Height - 1);
        var minX = Math.Clamp((int)Math.Floor(Math.Min(startDipX, endDipX) * scaleX), 0, image.Width - 1);
        var maxX = Math.Clamp((int)Math.Ceiling(Math.Max(startDipX, endDipX) * scaleX), 0, image.Width - 1);
        var rampPixels = 0;
        for (var px = minX; px <= maxX; px++)
        {
            if (PixelAt(image, px, py).A is > 15 and < 240)
            {
                rampPixels++;
            }
        }

        return rampPixels;
    }

    private static int MeasureVerticalEdgeRampPx(
        (byte[] Pixels, int Width, int Height) image,
        FrameworkElement element,
        double dipX,
        double startDipY,
        double endDipY)
    {
        var scaleX = image.Width / element.ActualWidth;
        var scaleY = image.Height / element.ActualHeight;
        var px = Math.Clamp((int)Math.Round(dipX * scaleX), 0, image.Width - 1);
        var minY = Math.Clamp((int)Math.Floor(Math.Min(startDipY, endDipY) * scaleY), 0, image.Height - 1);
        var maxY = Math.Clamp((int)Math.Ceiling(Math.Max(startDipY, endDipY) * scaleY), 0, image.Height - 1);
        var rampPixels = 0;
        for (var py = minY; py <= maxY; py++)
        {
            if (PixelAt(image, px, py).A is > 15 and < 240)
            {
                rampPixels++;
            }
        }

        return rampPixels;
    }

    private static bool IsRed(Color c) => c.A > 200 && c.R > 200 && c.G < 40 && c.B < 40;
    private static bool IsWhite(Color c) => c.A > 200 && c.R > 200 && c.G > 200 && c.B > 200;
    private static bool IsBlack(Color c) => c.A > 200 && c.R < 40 && c.G < 40 && c.B < 40;
    private static bool IsCyan(Color c) => c.A > 180 && c.R < 60 && c.G > 180 && c.B > 180;
    private static bool IsYellow(Color c) => c.A > 180 && c.R > 180 && c.G > 180 && c.B < 60;
}
