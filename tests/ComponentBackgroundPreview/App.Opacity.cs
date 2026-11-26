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
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Registry;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Models.Weather;
using OhMyLockScreen.ViewModels;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async void LaunchOpacityPreview()
    {
        var local = ApplicationData.Current.LocalFolder.Path;
        var report = Path.Combine(local, "opacity-report.txt");
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
                services.AddFeature<OpacityFeature, OpacitySettingsControl>();
                services.AddFeature<ShadowFeature, ShadowSettingsControl>();
                services.AddFeature<StrokeFeature, StrokeSettingsControl>();
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
                    new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(local, "OpacityWeatherSettings.json")),
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
                Height = 240,
                Background = new SolidColorBrush(Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            var backdropBoard = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 64, 128, 192)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var overlayRoot = new Grid
            {
                Padding = new Thickness(80, 60, 80, 60),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            captureRoot.Children.Add(backdropBoard);
            captureRoot.Children.Add(overlayRoot);
            windowRoot.Children.Add(captureRoot);
            Window.Current.Content = windowRoot;
            Window.Current.Activate();
            await Task.Delay(250);

            var rasterScale = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362)
                ? captureRoot.XamlRoot?.RasterizationScale ?? 1.0
                : 1.0;
            Check(rasterScale > 0, $"RasterizationScale telemetry (RasterizationScale={rasterScale})");

            // 1. Normal 模式不透明度 (100% / 50% / 0%) 与编组淡化（含阴影时阴影不透底）
            var boxLayout = new LayoutFeatureSettings
            {
                Width = 120,
                Height = 60,
                IsAutoWidth = false,
                IsAutoHeight = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            var boxOpacity = new OpacityFeatureSettings
            {
                Opacity = 1.0,
                BlendMode = OpacityBlendMode.Normal
            };
            var boxOpacityFeature = new FeatureInfo { Id = OpacityFeature.Id, Settings = boxOpacity };
            var boxBackground = new BackgroundFeatureSettings
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 192, 64, 128))
            };
            var boxShadow = new ShadowFeatureSettings
            {
                Color = Colors.Black,
                Opacity = 1.0,
                BlurRadius = 12,
                OffsetX = 0,
                OffsetY = 0
            };
            var boxShadowFeature = new FeatureInfo { Id = ShadowFeature.Id, Settings = boxShadow };
            var boxModel = new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = new TextComponentSettings { Text = "" },
                Features =
                [
                    new FeatureInfo { Id = "omls.features.layout.layout", Settings = boxLayout },
                    new FeatureInfo { Id = "omls.features.appearance.background", Settings = boxBackground },
                    boxShadowFeature,
                    boxOpacityFeature
                ]
            };
            var boxPresenter = new ComponentPresenter { Settings = boxModel };
            overlayRoot.Children.Add(boxPresenter);
            await Task.Delay(250);

            var normalFull = await CaptureComposedPixelsAsync(captureRoot);
            var fullCenter = SampleDip(normalFull, captureRoot, 140, 90);
            Check(NearColor(fullCenter, 192, 64, 128, 6),
                $"Normal 100% renders solid foreground cleanly (center={fullCenter})");

            boxOpacity.Opacity = 0.5;
            await Task.Delay(200);
            var normalHalf = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "opacity-normal-half-shadow.png");
            var halfCenter = SampleDip(normalHalf, captureRoot, 140, 90);
            var halfShadowEdge = SampleDip(normalHalf, captureRoot, 140, 122);
            // 若未作为统一编组淡化，位于内部的黑色阴影会先透叠变黑再与背景混合。
            Check(NearColor(halfCenter, 128, 96, 160, 6) && halfShadowEdge.R < 58 && halfShadowEdge.B < 180,
                $"Normal 50% fades content and shadow as a unified group without dark bleed inside box (center={halfCenter}, below={halfShadowEdge})");

            boxOpacity.Opacity = 0;
            await Task.Delay(200);
            var normalZero = await CaptureComposedPixelsAsync(captureRoot);
            var zeroCenter = SampleDip(normalZero, captureRoot, 140, 90);
            var zeroBelow = SampleDip(normalZero, captureRoot, 140, 126);
            Check(NearColor(zeroCenter, 64, 128, 192, 5) && NearColor(zeroBelow, 64, 128, 192, 5),
                $"Normal 0% hides both box and shadow (center={zeroCenter}, below={zeroBelow})");

            // 2. 全部 21 种混合方式编译与代表性精确像素检验
            boxModel.Features.Remove(boxShadowFeature);
            boxOpacity.Opacity = 1.0;
            await Task.Delay(150);

            var expectedModes = new Dictionary<OpacityBlendMode, Color>
            {
                [OpacityBlendMode.Darken] = Color.FromArgb(255, 64, 64, 128),
                [OpacityBlendMode.Multiply] = Color.FromArgb(255, 48, 32, 96),
                [OpacityBlendMode.ColorBurn] = Color.FromArgb(255, 1, 0, 130),
                [OpacityBlendMode.Lighten] = Color.FromArgb(255, 192, 128, 192),
                [OpacityBlendMode.Screen] = Color.FromArgb(255, 208, 160, 224),
                [OpacityBlendMode.ColorDodge] = Color.FromArgb(255, 255, 171, 255),
                [OpacityBlendMode.LinearDodge] = Color.FromArgb(255, 255, 192, 255),
                [OpacityBlendMode.Overlay] = Color.FromArgb(255, 96, 65, 192),
                [OpacityBlendMode.HardLight] = Color.FromArgb(255, 161, 64, 192),
                [OpacityBlendMode.Difference] = Color.FromArgb(255, 128, 64, 64),
                [OpacityBlendMode.Exclusion] = Color.FromArgb(255, 160, 128, 128),
                [OpacityBlendMode.Subtract] = Color.FromArgb(255, 0, 64, 64),
                [OpacityBlendMode.Division] = Color.FromArgb(255, 85, 255, 255)
            };

            foreach (var mode in OpacitySettingsViewModel.OrderedBlendModes)
            {
                if (mode == OpacityBlendMode.Normal)
                {
                    continue;
                }

                boxOpacity.BlendMode = mode;
                await Task.Delay(80);
                var blendedImage = await CaptureComposedPixelsAsync(captureRoot);
                var sampled = SampleDip(blendedImage, captureRoot, 140, 90);
                var cornerBg = SampleDip(blendedImage, captureRoot, 20, 20);
                Check(sampled.A == 255 && NearColor(cornerBg, 64, 128, 192, 5),
                    $"Blend mode {mode} renders without exception and preserves surrounding backdrop (center={sampled}, corner={cornerBg})");

                if (expectedModes.TryGetValue(mode, out var expected))
                {
                    Check(NearColor(sampled, expected.R, expected.G, expected.B, 10),
                        $"Blend mode {mode} matches expected RGB {expected} (actual={sampled})");
                }
            }

            // 3. 半透明前景 + 半透明背景：验证无矩形包围盒二次变暗或重影
            backdropBoard.Background = new SolidColorBrush(Color.FromArgb(128, 64, 128, 192));
            boxBackground.Background = new SolidColorBrush(Color.FromArgb(255, 192, 64, 128));
            boxOpacity.BlendMode = OpacityBlendMode.Screen;
            boxOpacity.Opacity = 0.5;
            var boxBorder = new BorderFeatureSettings
            {
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(24)
            };
            var boxBorderFeature = new FeatureInfo { Id = "omls.features.appearance.border", Settings = boxBorder };
            boxModel.Features.Add(boxBorderFeature);
            await Task.Delay(250);

            var semiBgImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "opacity-semibg-screen-half.png");
            var semiCornerInsideBounds = SampleDip(semiBgImage, captureRoot, 82, 62);
            var semiOutsideBounds = SampleDip(semiBgImage, captureRoot, 20, 20);
            var semiCenter = SampleDip(semiBgImage, captureRoot, 140, 90);
            Check(semiCornerInsideBounds == semiOutsideBounds && semiCenter.A > semiOutsideBounds.A,
                $"Rounded transparent corners inside bounding box match semi-transparent backdrop without double-darkening ghosting (corner={semiCornerInsideBounds}, outside={semiOutsideBounds}, center={semiCenter})");

            // 还原不透明背景与直角，测试混合 + 外描边 + 阴影 + 编辑条纹遮罩（高 DPI 嵌套检查）
            backdropBoard.Background = new SolidColorBrush(Color.FromArgb(255, 40, 80, 120));
            boxModel.Features.Remove(boxBorderFeature);

            // 4. Opacity(Screen, 0.85) + Outside Stroke(Green, 8) + Shadow(Magenta, Blur=6, OffsetY=18) + HideByRules(IsEditing=true)
            var ruleSettings = new HideByRulesSettings();
            ruleSettings.Ruleset.Groups[0].Rules[0].Id = BuiltinRules.PlayingId;
            boxBackground.Background = new SolidColorBrush(Colors.White);
            boxOpacity.BlendMode = OpacityBlendMode.Difference;
            boxOpacity.Opacity = 0.9;
            boxModel.Features.Add(new FeatureInfo
            {
                Id = StrokeFeature.Id,
                Settings = new StrokeFeatureSettings
                {
                    StrokeBrush = new SolidColorBrush(Color.FromArgb(255, 0, 255, 0)),
                    Thickness = 8,
                    IsHollow = false,
                    Position = StrokePosition.Outside
                }
            });
            boxShadow.Color = Color.FromArgb(255, 255, 0, 255);
            boxShadow.Opacity = 1.0;
            boxShadow.BlurRadius = 6;
            boxShadow.OffsetX = 0;
            boxShadow.OffsetY = 18;
            boxModel.Features.Add(boxShadowFeature);
            boxModel.Features.Add(new FeatureInfo { Id = "omls.features.visibility.hideByRules", Settings = ruleSettings });
            boxPresenter.IsEditing = true;
            await Task.Delay(300);

            var comboImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "opacity-combo-stroke-shadow-stripes.png");

            // 白色(255,255,255) 叠在洋红阴影(255,0,255) 上，R 通道恒定而 G 通道随条纹透明度剧烈变化。
            var stripeSamples = Enumerable.Range(100, 50).Select(x => SampleDip(comboImage, captureRoot, x, 90)).ToArray();
            var centerDeltaG = stripeSamples.Max(p => (int)p.G) - stripeSamples.Min(p => (int)p.G);
            // 上方 Outside 描边（绿色 0,255,0 与背景 40,80,120 做 Difference => R~40, G~175, B~120，绿色明显高于蓝色/红色）
            var topStrokeSamples = Enumerable.Range(100, 40).Select(x => SampleDip(comboImage, captureRoot, x, 56)).ToArray();
            var strokePresent = topStrokeSamples.Any(p => p.G > 135 && p.G > p.R + 40);
            // 下方洋红阴影（255,0,255 与背景 40,80,120 做 Difference => R 显著升高、G 保持或变化、B 显著高于背景）
            var shadowBelowPixels = CountPixelsInDipRect(comboImage, captureRoot, 90, 130, 190, 146,
                p => p.R > 95 && p.G < 120);
            Check(centerDeltaG > 35 && strokePresent && shadowBelowPixels > 20,
                $"Opacity(Difference) preserves interior diagonal stripes, unclipped outside stroke, and unclipped shadow (stripeDeltaG={centerDeltaG}, strokePresent={strokePresent}, shadowPixels={shadowBelowPixels})");

            // 5. TransformFeature (旋转 45 度) + Opacity(Difference) 验证背景对齐
            overlayRoot.Children.Clear();
            backdropBoard.Background = new SolidColorBrush(Color.FromArgb(255, 64, 128, 192));
            var rotatedBox = new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = new TextComponentSettings { Text = "" },
                Features =
                [
                    new FeatureInfo { Id = "omls.features.layout.layout", Settings = boxLayout },
                    new FeatureInfo { Id = "omls.features.appearance.background", Settings = new BackgroundFeatureSettings { Background = new SolidColorBrush(Colors.White) } },
                    new FeatureInfo { Id = "omls.features.layout.transform", Settings = new TransformFeatureSettings { Rotation = 45 } },
                    new FeatureInfo { Id = OpacityFeature.Id, Settings = new OpacityFeatureSettings { Opacity = 1.0, BlendMode = OpacityBlendMode.Difference } }
                ]
            };
            var rotatedPresenter = new ComponentPresenter { Settings = rotatedBox };
            overlayRoot.Children.Add(rotatedPresenter);
            await Task.Delay(250);

            var rotatedImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "opacity-transform-difference.png");
            var rotatedCenter = SampleDip(rotatedImage, captureRoot, 140, 90);
            var rotatedOutside = SampleDip(rotatedImage, captureRoot, 195, 65);
            // 白色(255) 与背景 (64, 128, 192) 的 Difference = (191, 127, 63)
            Check(NearColor(rotatedCenter, 191, 127, 63, 10) && NearColor(rotatedOutside, 64, 128, 192, 6),
                $"Rotated TransformFeature + Difference aligns blended pixels cleanly with backdrop (center={rotatedCenter}, cornerOutsideRotated={rotatedOutside})");

            // 6. LayerPresenter（含子组件阴影与嵌套子组件 OpacityFeature）与 ScenePresenter 不透明度及混合方式
            overlayRoot.Children.Clear();
            var childBox = MakeWhiteBox(80, 48);
            childBox.Features.Add(new FeatureInfo
            {
                Id = ShadowFeature.Id,
                Settings = new ShadowFeatureSettings
                {
                    Color = Color.FromArgb(255, 255, 255, 0),
                    Opacity = 1.0,
                    BlurRadius = 6,
                    OffsetX = 0,
                    OffsetY = 16
                }
            });
            var nestedOpacityBox = MakeWhiteBox(60, 48);
            ((LayoutFeatureSettings)nestedOpacityBox.Features[0].Settings!).Margin = new Thickness(110, 0, 0, 0);
            nestedOpacityBox.Features.Add(new FeatureInfo
            {
                Id = OpacityFeature.Id,
                Settings = new OpacityFeatureSettings
                {
                    Opacity = 0.5,
                    BlendMode = OpacityBlendMode.Normal
                }
            });
            var layerModel = new LayerSettings
            {
                Kind = LayoutKind.Canvas,
                Features =
                [
                    new FeatureInfo
                    {
                        Id = OpacityFeature.Id,
                        Settings = new OpacityFeatureSettings
                        {
                            Opacity = 1.0,
                            BlendMode = OpacityBlendMode.Difference
                        }
                    }
                ],
                Children = [childBox, nestedOpacityBox]
            };
            var layerPresenter = new LayerPresenter
            {
                Layer = layerModel,
                Width = 220,
                Height = 140,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            overlayRoot.Children.Add(layerPresenter);
            await Task.Delay(300);

            var layerImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "opacity-layer-childshadow.png");
            var layerBoxCenter = SampleDip(layerImage, captureRoot, 120, 84);
            var nestedBoxCenter = SampleDip(layerImage, captureRoot, 220, 84);
            // 黄色阴影 (255,255,0) 与 (64,128,192) 做 Difference => (191, 127, 192)；下方应检测到阴影外扩未被裁剪。
            var layerShadowCount = CountPixelsInDipRect(layerImage, captureRoot, 85, 114, 155, 130,
                p => p.R > 140 && p.B > 170);
            Check(NearColor(layerBoxCenter, 191, 127, 63, 10)
                && layerShadowCount > 15
                && NearColor(nestedBoxCenter, 128, 128, 128, 8),
                $"LayerPresenter Opacity(Difference) blends child box, preserves child shadow outset, and composes nested child Opacity(0.5) (center={layerBoxCenter}, shadowPixels={layerShadowCount}, nested={nestedBoxCenter})");

            // 清理并测试 ScenePresenter
            overlayRoot.Children.Clear();
            var sceneBox = MakeWhiteBox(100, 50);
            var sceneOpacityFeature = new FeatureInfo
            {
                Id = OpacityFeature.Id,
                Settings = new OpacityFeatureSettings { Opacity = 0.5, BlendMode = OpacityBlendMode.Screen }
            };
            var sceneModel = new SceneSettings
            {
                Background = new BackgroundSettings { Mode = BackgroundMode.None },
                Features = [sceneOpacityFeature],
                Layers = [new LayerSettings { Kind = LayoutKind.Canvas, Children = [sceneBox] }]
            };
            var scenePresenter = new ScenePresenter
            {
                Scene = sceneModel,
                Width = 160,
                Height = 80,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            overlayRoot.Children.Add(scenePresenter);
            await Task.Delay(300);

            var sceneImage = await CaptureComposedPixelsAsync(captureRoot);
            await CaptureAsync(captureRoot, "opacity-scene-screen.png");
            var sceneCenter = SampleDip(sceneImage, captureRoot, 120, 85);
            // White(255) Screen BG(64,128,192) = (255,255,255), at Opacity=0.5 => 0.5*(255,255,255) + 0.5*(64,128,192) = (160, 192, 224)
            Check(NearColor(sceneCenter, 160, 192, 224, 10),
                $"ScenePresenter Opacity(Screen, 0.5) blends with underlying backdrop (center={sceneCenter})");

            sceneModel.Features.Remove(sceneOpacityFeature);
            await Task.Delay(200);
            var restoredSceneImage = await CaptureComposedPixelsAsync(captureRoot);
            Check(NearColor(SampleDip(restoredSceneImage, captureRoot, 120, 85), 255, 255, 255, 5),
                "Removing ScenePresenter OpacityFeature restores full opaque white box");

            // 7. OpacitySettingsControl 原生 UI 冒烟检查
            var editorInfo = new FeatureInfo { Id = OpacityFeature.Id };
            var control = (OpacitySettingsControl)IAppHost.GetRequiredService<IFeatureService>().CreateSettingsControl(editorInfo)!;
            windowRoot.Children.Add(control);
            control.HorizontalAlignment = HorizontalAlignment.Right;
            control.Width = 320;
            await Task.Delay(150);

            var opacityInput = (Microsoft.UI.Xaml.Controls.NumberBox)control.FindName("OpacityInput");
            var opacitySlider = (Slider)control.FindName("OpacitySlider");
            var blendCombo = (ComboBox)control.FindName("BlendModeComboBox");

            Check(blendCombo.Items.Count == 22 && blendCombo.SelectedIndex == 0 && opacitySlider.Value == 100,
                $"OpacitySettingsControl initialized with all 22 blend modes (count={blendCombo.Items.Count}, index={blendCombo.SelectedIndex}, slider={opacitySlider.Value})");

            opacityInput.Value = 0.42;
            blendCombo.SelectedIndex = 7; // Screen
            await Task.Delay(100);

            var edited = editorInfo.GetSettings<OpacityFeatureSettings>();
            Check(Math.Abs(edited.Opacity - 0.42) < 0.001
                && edited.BlendMode == OpacityBlendMode.Screen
                && opacitySlider.Value == 42,
                $"OpacitySettingsControl edits synchronize to settings and slider (opacity={edited.Opacity}, mode={edited.BlendMode}, slider={opacitySlider.Value})");

            var resetButton = (Button)control.FindName("ResetButton");
            ((IInvokeProvider)new ButtonAutomationPeer(resetButton).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            Check(edited.Opacity == 1.0 && edited.BlendMode == OpacityBlendMode.Normal
                && blendCombo.SelectedIndex == 0 && opacitySlider.Value == 100 && opacityInput.Value == 1.0,
                $"OpacitySettingsControl reset button restores defaults and UI controls (opacity={edited.Opacity}, mode={edited.BlendMode}, combo={blendCombo.SelectedIndex}, slider={opacitySlider.Value})");
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

    private static bool NearColor(Color actual, byte expectedR, byte expectedG, byte expectedB, int tolerance)
        => Math.Abs(actual.R - expectedR) <= tolerance
            && Math.Abs(actual.G - expectedG) <= tolerance
            && Math.Abs(actual.B - expectedB) <= tolerance;
}
