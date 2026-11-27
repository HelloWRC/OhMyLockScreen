using System.Diagnostics;
using System.Net.Http;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graphics.Canvas;
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
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async void LaunchParallaxPreview()
    {
        var local = ApplicationData.Current.LocalFolder.Path;
        var report = Path.Combine(local, "parallax-report.txt");
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
                    new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(local, "ParallaxWeatherSettings.json")),
                    new HttpClient(), TimeProvider.System, NullLogger<WeatherService>.Instance));
                services.AddRule<CurrentWeatherRuleSettings, CurrentWeatherRuleControl>(BuiltinRules.WeatherId, "当前天气是");
                services.AddRule<RainTimeRuleSettings, RainTimeRuleControl>(BuiltinRules.RainId, "距离降水开始/结束还剩", BuiltinRules.RainIcon);
                services.AddRule(BuiltinRules.PlayingId, "有媒体正在播放", "\uE768", _ => playing);
                services.AddRule<MediaFieldRuleSettings, MediaFieldRuleControl>(BuiltinRules.FieldId, "媒体字段是", "\uE8D6", _ => false);
            }).Build();
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(SceneSettings).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(App).Assembly);

            await RunParallaxDwmRegressionAsync(report);
            await RunSelectionAlignmentDwmChecksAsync(report);

            var parallaxSettings = new ParallaxFeatureSettings();
            var scene = new SceneSettings
            {
                Background = new BackgroundSettings
                {
                    Mode = BackgroundMode.None,
                    ParallaxStrength = 100
                },
                Layers =
                [
                    new LayerSettings
                    {
                        Name = "ParallaxLayer",
                        Kind = LayoutKind.Grid,
                        Features =
                        [
                            new FeatureInfo { Id = "omls.features.layout.parallax", Settings = parallaxSettings },
                            new FeatureInfo { Id = OpacityFeature.Id, Settings = new OpacityFeatureSettings { Opacity = 0.9 } },
                            new FeatureInfo { Id = ShadowFeature.Id, Settings = new ShadowFeatureSettings { BlurRadius = 12, OffsetY = 8 } },
                            new FeatureInfo { Id = "omls.features.layout.transform", Settings = new TransformFeatureSettings { TranslateX = 12, Rotation = 5 } }
                        ],
                        Children =
                        [
                            new ComponentSettings
                            {
                                Id = TextComponent.Id,
                                Settings = new TextComponentSettings { Text = "Composition Parallax" },
                                Features =
                                [
                                    new FeatureInfo { Id = "omls.features.appearance.text", Settings = new TextFeatureSettings { FontSize = 40 } },
                                    new FeatureInfo { Id = "omls.features.appearance.foreground", Settings = new ForegroundFeatureSettings { Foreground = new SolidColorBrush(Colors.White) } },
                                    new FeatureInfo
                                    {
                                        Id = "omls.features.layout.layout",
                                        Settings = new LayoutFeatureSettings
                                        {
                                            Width = 420,
                                            Height = 96,
                                            IsAutoWidth = false,
                                            IsAutoHeight = false,
                                            HorizontalAlignment = HorizontalAlignment.Center,
                                            VerticalAlignment = VerticalAlignment.Center
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                ]
            };

            var windowRoot = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 18, 28, 44)),
                Width = 1024,
                Height = 768
            };
            var presenter = new ScenePresenter
            {
                Width = 1024,
                Height = 768,
                Background = new SolidColorBrush(Color.FromArgb(255, 32, 64, 112)),
                Scene = scene,
                ParallaxInputSurface = windowRoot
            };
            windowRoot.Children.Add(presenter);
            Window.Current.Content = windowRoot;
            Window.Current.Activate();
            await Task.Delay(250);

            var bgHost = (FrameworkElement)presenter.FindName("BackgroundParallaxHost");
            var bgVisual = ElementCompositionPreview.GetElementVisual(bgHost);
            var layerPresenter = FindFirst<LayerPresenter>(presenter)!;
            var parallaxWrapper = layerPresenter.Decorators.Elements.OfType<Border>().First();
            var layerVisual = ElementCompositionPreview.GetElementVisual(parallaxWrapper);

            Check(bgVisual.Compositor != null
                && layerVisual.Compositor != null
                && layerVisual.Properties.TryGetVector3("Translation", out _) == CompositionGetValueStatus.Succeeded,
                "Real DWM Compositor attached Translation PropertySet and expressions without exceptions");

            var updatedEvents = 0;
            presenter.LayerParallaxUpdated += (_, _) => updatedEvents++;

            presenter.SampleParallaxPoint(new Point(1024, 0));
            Check(presenter.HasActiveLayerParallaxAnimation && updatedEvents > 0,
                "SampleParallaxPoint starts active Composition layer parallax animation");

            await Task.Delay(1150);
            Check(!presenter.HasActiveLayerParallaxAnimation,
                "Layer parallax analytical state settles after 1000ms duration");
            Check(presenter.TryGetLayerParallaxMatrix(parallaxWrapper, out var matrix100),
                "TryGetLayerParallaxMatrix resolves settled matrix at 100% strength");
            Check(Math.Abs(matrix100.M11 - 1.02) < 0.001 && Math.Abs(matrix100.X - (4.104783 - 512 * 0.02)) < 0.1,
                $"Settled matrix matches expected scale & translation (M11={matrix100.M11:F4}, X={matrix100.X:F4})");

            // Strength transitions (200 -> 0 -> 100) and layer override
            scene.Background.ParallaxStrength = 200;
            await Task.Delay(1150);
            Check(presenter.TryGetLayerParallaxMatrix(parallaxWrapper, out var matrix200) && Math.Abs(matrix200.M11 - 1.04) < 0.001,
                $"200% strength updates scale to 1.04 (M11={matrix200.M11:F4})");

            parallaxSettings.OverrideStrength = true;
            parallaxSettings.Strength = 50;
            await Task.Delay(1150);
            Check(presenter.TryGetLayerParallaxMatrix(parallaxWrapper, out var matrix50) && Math.Abs(matrix50.M11 - 1.01) < 0.001,
                $"Layer 50% override sets independent scale to 1.01 (M11={matrix50.M11:F4})");

            parallaxSettings.OverrideStrength = false;
            scene.Background.ParallaxStrength = 0;
            await Task.Delay(150);
            Check(presenter.TryGetLayerParallaxMatrix(parallaxWrapper, out var matrix0) && matrix0 == OhMyLockScreen.Shared.Helpers.UI.BrushEditorMatrix.Identity,
                "0% strength resets layer parallax matrix to identity immediately");

            scene.Background.ParallaxStrength = 100;
            await Task.Delay(1150);

            // Viewport resize
            presenter.Width = 640;
            presenter.Height = 480;
            windowRoot.UpdateLayout();
            await Task.Delay(250);
            Check(presenter.TryGetLayerParallaxMatrix(parallaxWrapper, out var resizedMatrix) && Math.Abs(resizedMatrix.M11 - 1.02) < 0.002,
                $"Viewport resize maintains valid parallax transform (M11={resizedMatrix.M11:F4})");
            presenter.Width = 1024;
            presenter.Height = 768;
            windowRoot.UpdateLayout();
            await Task.Delay(250);

            // Capture non-empty render before unlock tests
            await CaptureAsync(windowRoot, "parallax-preview.png");
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(windowRoot);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            var hasNonEmpty = false;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] > 0)
                {
                    hasNonEmpty = true;
                    break;
                }
            }
            Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0 && hasNonEmpty,
                $"Rendered parallax scene is non-empty ({bitmap.PixelWidth}x{bitmap.PixelHeight})");

            // Drag (mid-flight freeze), rebound, and unlock dismissal
            presenter.SampleParallaxPoint(new Point(0, 768));
            presenter.SetUnlockDragOffset(90);
            await Task.Delay(50);
            presenter.PlayUnlockReturnAnimation();
            await Task.Delay(250);
            await presenter.PlayUnlockAnimationAsync();
            Check(true, "Mid-flight freeze, drag, rebound, and Composition unlock dismissal completed without error");

            // Unload and reload visual tree cleanly
            windowRoot.Children.Remove(presenter);
            await Task.Delay(150);
            Check(bgVisual.Scale == Vector3.One && bgVisual.CenterPoint == Vector3.Zero
                && bgVisual.Properties.TryGetVector3("Translation", out var unloadedT) == CompositionGetValueStatus.Succeeded
                && unloadedT == Vector3.Zero,
                "Unloading ScenePresenter resets Composition visual properties to identity");

            windowRoot.Children.Add(presenter);
            await Task.Delay(250);
            presenter.SampleParallaxPoint(new Point(1024, 384));
            await Task.Delay(1150);
            var reloadedLayer = FindFirst<LayerPresenter>(presenter)!;
            var reloadedWrapper = reloadedLayer.Decorators.Elements.OfType<Border>().First();
            Check(presenter.TryGetLayerParallaxMatrix(reloadedWrapper, out var reloadedMatrix) && Math.Abs(reloadedMatrix.M11 - 1.02) < 0.002,
                $"Reloading ScenePresenter reattaches Composition parallax cleanly (M11={reloadedMatrix.M11:F4})");

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

    private sealed record CapturedFrame(
        double ElapsedMs,
        double SysRelativeMs,
        double CenterX,
        double CenterY,
        int BoxWidth,
        int BoxHeight,
        double EffectiveSize,
        int WhitePixels);

    private sealed record ColorBoxStats(
        double CenterX,
        double CenterY,
        int MinX,
        int MaxX,
        int MinY,
        int MaxY,
        int PixelCount);

    private sealed record DualColorFrame(
        double ElapsedMs,
        int FrameWidth,
        int FrameHeight,
        ColorBoxStats Red,
        ColorBoxStats Cyan);

    private async Task RunSelectionAlignmentDwmChecksAsync(string reportPath)
    {
        _report.Add("DWM selection-alignment regression checks");
        File.WriteAllLines(reportPath, _report);

        static SceneSettings CreateAlignmentScene(
            HorizontalAlignment hAlign,
            VerticalAlignment vAlign,
            Thickness margin,
            bool addLayerTransform)
        {
            System.Collections.ObjectModel.ObservableCollection<FeatureInfo> layerFeatures =
            [
                new FeatureInfo { Id = "omls.features.layout.parallax", Settings = new ParallaxFeatureSettings() }
            ];
            if (addLayerTransform)
            {
                layerFeatures.Add(new FeatureInfo
                {
                    Id = "omls.features.layout.transform",
                    Settings = new TransformFeatureSettings
                    {
                        TranslateX = 30,
                        TranslateY = 20,
                        Rotation = 10,
                        ScaleX = 1.1,
                        ScaleY = 1.1
                    }
                });
            }

            return new SceneSettings
            {
                Background = new BackgroundSettings
                {
                    Mode = BackgroundMode.None,
                    ParallaxStrength = 200
                },
                Layers =
                [
                    new LayerSettings
                    {
                        Name = "AlignmentLayer",
                        Kind = LayoutKind.Grid,
                        Features = layerFeatures,
                        Children =
                        [
                            new ComponentSettings
                            {
                                Id = TextComponent.Id,
                                Settings = new TextComponentSettings { Text = "" },
                                Features =
                                [
                                    new FeatureInfo
                                    {
                                        Id = "omls.features.appearance.background",
                                        Settings = new BackgroundFeatureSettings { Background = new SolidColorBrush(Colors.Red) }
                                    },
                                    new FeatureInfo
                                    {
                                        Id = "omls.features.layout.layout",
                                        Settings = new LayoutFeatureSettings
                                        {
                                            Width = 240,
                                            Height = 80,
                                            IsAutoWidth = false,
                                            IsAutoHeight = false,
                                            IsAutoAlignment = true,
                                            HorizontalAlignment = hAlign,
                                            VerticalAlignment = vAlign,
                                            Margin = margin
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                ]
            };
        }

        static OhMyLockScreen.Shared.Helpers.UI.BrushEditorMatrix FromGeneralTransform(GeneralTransform? transform)
        {
            if (transform == null)
            {
                return OhMyLockScreen.Shared.Helpers.UI.BrushEditorMatrix.Identity;
            }

            try
            {
                var origin = transform.TransformPoint(new Point(0, 0));
                var pointX = transform.TransformPoint(new Point(100, 0));
                var pointY = transform.TransformPoint(new Point(0, 100));
                var matrix = new OhMyLockScreen.Shared.Helpers.UI.BrushEditorMatrix(
                    (pointX.X - origin.X) / 100,
                    (pointX.Y - origin.Y) / 100,
                    (pointY.X - origin.X) / 100,
                    (pointY.Y - origin.Y) / 100,
                    origin.X,
                    origin.Y);
                return double.IsFinite(matrix.M11) && double.IsFinite(matrix.X) && double.IsFinite(matrix.Y)
                    ? matrix
                    : OhMyLockScreen.Shared.Helpers.UI.BrushEditorMatrix.Identity;
            }
            catch
            {
                return OhMyLockScreen.Shared.Helpers.UI.BrushEditorMatrix.Identity;
            }
        }

        static GeneralTransform? TryTransform(UIElement from, UIElement to)
        {
            try { return from.TransformToVisual(to); }
            catch { return null; }
        }

        static ColorBoxStats MeasureColorBox(byte[] pixels, int width, int height, Func<byte, byte, byte, bool> match, Func<byte, byte, byte, double> weight)
        {
            double sumW = 0, sumX = 0, sumY = 0;
            var minX = int.MaxValue;
            var maxX = int.MinValue;
            var minY = int.MaxValue;
            var maxY = int.MinValue;
            var count = 0;

            for (var y = 0; y < height; y++)
            {
                var row = y * width * 4;
                for (var x = 0; x < width; x++)
                {
                    var idx = row + x * 4;
                    var b = pixels[idx];
                    var g = pixels[idx + 1];
                    var r = pixels[idx + 2];
                    if (!match(r, g, b))
                    {
                        continue;
                    }

                    var w = weight(r, g, b);
                    count++;
                    sumW += w;
                    sumX += x * w;
                    sumY += y * w;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            return new ColorBoxStats(
                sumW > 0 ? sumX / sumW : double.NaN,
                sumW > 0 ? sumY / sumW : double.NaN,
                minX, maxX, minY, maxY, count);
        }

        static async Task<DualColorFrame> CaptureSingleDualFrameAsync(FrameworkElement captureTarget)
        {
            captureTarget.UpdateLayout();
            await Task.Delay(120);
            var visual = ElementCompositionPreview.GetElementVisual(captureTarget);
            var item = GraphicsCaptureItem.CreateFromVisual(visual);
            var size = item.Size.Width > 0 && item.Size.Height > 0
                ? item.Size
                : new SizeInt32((int)Math.Ceiling(captureTarget.ActualWidth), (int)Math.Ceiling(captureTarget.ActualHeight));
            var device = CanvasDevice.GetSharedDevice();
            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            using var session = pool.CreateCaptureSession(item);
            var tcs = new TaskCompletionSource<DualColorFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnArrived(Direct3D11CaptureFramePool sender, object _)
            {
                while (true)
                {
                    using var frame = sender.TryGetNextFrame();
                    if (frame is null) break;

                    using var bitmap = CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Surface);
                    var pixels = bitmap.GetPixelBytes();
                    var w = (int)bitmap.SizeInPixels.Width;
                    var h = (int)bitmap.SizeInPixels.Height;
                    var red = MeasureColorBox(pixels, w, h, (r, g, b) => r > 140 && g < 90 && b < 90, (r, _, _) => r);
                    var cyan = MeasureColorBox(pixels, w, h, (r, g, b) => g > 140 && b > 140 && r < 90, (_, g, b) => (g + b) * 0.5);
                    tcs.TrySetResult(new DualColorFrame(0, w, h, red, cyan));
                }
            }

            pool.FrameArrived += OnArrived;
            session.StartCapture();
            try
            {
                return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
            }
            finally
            {
                pool.FrameArrived -= OnArrived;
            }
        }

        var pageRoot = new Grid
        {
            Background = new SolidColorBrush(Colors.Black),
            Width = 1200,
            Height = 768
        };
        var sceneViewport = new Grid
        {
            Background = new SolidColorBrush(Colors.Transparent)
        };
        var presenter = new ScenePresenter
        {
            IsEditing = true,
            SuppressParallaxWithoutBackground = true,
            ParallaxInputSurface = pageRoot
        };
        var selectionCanvas = new Canvas { IsHitTestVisible = false };
        var outline = new Windows.UI.Xaml.Shapes.Polygon
        {
            Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 255, 255)),
            StrokeThickness = 2
        };
        selectionCanvas.Children.Add(outline);
        sceneViewport.Children.Add(presenter);
        sceneViewport.Children.Add(selectionCanvas);
        pageRoot.Children.Add(sceneViewport);

        Window.Current.Content = pageRoot;
        Window.Current.Activate();
        await Task.Delay(250);

        Point[] UpdateDirectOutlineFor(FrameworkElement target)
        {
            var m = FromGeneralTransform(TryTransform(target, sceneViewport));
            var tl = m.Map(new Point(0, 0));
            var tr = m.Map(new Point(target.ActualWidth, 0));
            var br = m.Map(new Point(target.ActualWidth, target.ActualHeight));
            var bl = m.Map(new Point(0, target.ActualHeight));
            outline.Points = new PointCollection { tl, tr, br, bl };
            return [tl, tr, br, bl];
        }

        async Task VerifyAlignmentAsync(string name, FrameworkElement target, double maxDipError, bool updateOutline = true)
        {
            target.UpdateLayout();
            sceneViewport.UpdateLayout();
            await Task.Delay(60);

            var quad = updateOutline
                ? UpdateDirectOutlineFor(target)
                : [outline.Points[0], outline.Points[1], outline.Points[2], outline.Points[3]];
            await Task.Delay(60);

            var frame = await CaptureSingleDualFrameAsync(sceneViewport);
            var sx = frame.FrameWidth / sceneViewport.ActualWidth;
            var sy = frame.FrameHeight / sceneViewport.ActualHeight;
            var redCx = frame.Red.CenterX / sx;
            var redCy = frame.Red.CenterY / sy;
            var redMinX = frame.Red.MinX / sx;
            var redMaxX = (frame.Red.MaxX + 1) / sx;
            var redMinY = frame.Red.MinY / sy;
            var redMaxY = (frame.Red.MaxY + 1) / sy;

            var quadCx = (quad[0].X + quad[1].X + quad[2].X + quad[3].X) * 0.25;
            var quadCy = (quad[0].Y + quad[1].Y + quad[2].Y + quad[3].Y) * 0.25;
            var quadMinX = Math.Min(Math.Min(quad[0].X, quad[1].X), Math.Min(quad[2].X, quad[3].X));
            var quadMaxX = Math.Max(Math.Max(quad[0].X, quad[1].X), Math.Max(quad[2].X, quad[3].X));
            var quadMinY = Math.Min(Math.Min(quad[0].Y, quad[1].Y), Math.Min(quad[2].Y, quad[3].Y));
            var quadMaxY = Math.Max(Math.Max(quad[0].Y, quad[1].Y), Math.Max(quad[2].Y, quad[3].Y));

            var strokeHalf = outline.StrokeThickness * 0.5;
            var dCx = Math.Abs(quadCx - redCx);
            var dCy = Math.Abs(quadCy - redCy);
            var cyanBoxCx = (frame.Cyan.MinX + frame.Cyan.MaxX + 1) * 0.5 / sx;
            var cyanBoxCy = (frame.Cyan.MinY + frame.Cyan.MaxY + 1) * 0.5 / sy;
            var dCyanRedCx = Math.Abs(cyanBoxCx - redCx);
            var dCyanRedCy = Math.Abs(cyanBoxCy - redCy);
            var dMinX = Math.Abs(redMinX - (quadMinX + strokeHalf));
            var dMaxX = Math.Abs(redMaxX - (quadMaxX - strokeHalf));
            var dMinY = Math.Abs(redMinY - (quadMinY + strokeHalf));
            var dMaxY = Math.Abs(redMaxY - (quadMaxY - strokeHalf));
            var maxEdgeErr = Math.Max(Math.Max(dMinX, dMaxX), Math.Max(dMinY, dMaxY));

            _report.Add(
                $"DIAG [{name}]: dCenter=({dCx:F2},{dCy:F2}), dCyanRed=({dCyanRedCx:F2},{dCyanRedCy:F2}), maxEdgeErr={maxEdgeErr:F2} " +
                $"| Quad X=[{quadMinX:F2}..{quadMaxX:F2}] Y=[{quadMinY:F2}..{quadMaxY:F2}] " +
                $"vs RED X=[{redMinX:F2}..{redMaxX:F2}] Y=[{redMinY:F2}..{redMaxY:F2}] (cyanPx={frame.Cyan.PixelCount})");

            Check(frame.Red.PixelCount > 5000 && frame.Cyan.PixelCount > 100
                && dCx < 1.0 && dCy < 1.0
                && dCyanRedCx < 1.0 && dCyanRedCy < 1.0
                && maxEdgeErr < maxDipError,
                $"{name} aligns TransformToVisual selection quad with rendered DWM component bounds " +
                $"(dCx={dCx:F2}, dCy={dCy:F2}, dCyanRed=({dCyanRedCx:F2},{dCyanRedCy:F2}), maxEdge={maxEdgeErr:F2} < {maxDipError:F1})");
            File.WriteAllLines(reportPath, _report);
        }

        try
        {
            // 1. Centered full viewport (wait >= 1100ms for 1000ms ScaleDuration settle)
            sceneViewport.Margin = new Thickness(0);
            presenter.BackgroundViewportSize = new Size(1200, 768);
            presenter.Scene = CreateAlignmentScene(HorizontalAlignment.Center, VerticalAlignment.Center, default, addLayerTransform: false);
            await Task.Delay(350);
            presenter.SampleParallaxPoint(new Point(1150, 50));
            await Task.Delay(1100);
            var comp1 = FindFirst<ComponentPresenter>(presenter)!;
            await VerifyAlignmentAsync("Align_Centered_FullViewport", comp1, maxDipError: 1.2);

            // 2. Off-center component with side panel inset + pointer inside right side panel
            sceneViewport.Margin = new Thickness(200, 0, 200, 0);
            presenter.BackgroundViewportSize = new Size(1200, 768);
            presenter.Scene = CreateAlignmentScene(HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(60, 50, 0, 0), addLayerTransform: false);
            await Task.Delay(350);
            presenter.SampleParallaxPoint(new Point(1150, 50));
            await Task.Delay(1100);
            var comp2 = FindFirst<ComponentPresenter>(presenter)!;
            await VerifyAlignmentAsync("Align_OffCenter_SidePanelsInset", comp2, maxDipError: 1.2);

            // 3. Layer with BOTH ParallaxFeature AND TransformFeature (centered so unclipped inside 800x768)
            presenter.Scene = CreateAlignmentScene(HorizontalAlignment.Center, VerticalAlignment.Center, default, addLayerTransform: true);
            await Task.Delay(350);
            presenter.SampleParallaxPoint(new Point(1150, 50));
            await Task.Delay(1100);
            var comp3 = FindFirst<ComponentPresenter>(presenter)!;
            await VerifyAlignmentAsync("Align_LayerParallaxAndTransform", comp3, maxDipError: 1.5);

            // 4. Mid-motion 2ms sweep + 100ms trailing settle grace (must unhook automatically and match settled RED box)
            presenter.Scene = CreateAlignmentScene(HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(60, 50, 0, 0), addLayerTransform: false);
            await Task.Delay(350);
            presenter.SampleParallaxPoint(new Point(50, 700));
            await Task.Delay(1100);
            var comp4 = FindFirst<ComponentPresenter>(presenter)!;
            UpdateDirectOutlineFor(comp4);
            await Task.Delay(60);

            var tracking = false;
            long graceDeadline = 0;
            void ExtendGrace() => graceDeadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 0.1);
            void UpdateGraceTracking()
            {
                var now = Stopwatch.GetTimestamp();
                var active = presenter.HasActiveLayerParallaxAnimation;
                if (active)
                {
                    ExtendGrace();
                }

                var shouldTrack = active || now < graceDeadline;
                if (shouldTrack == tracking)
                {
                    return;
                }

                tracking = shouldTrack;
                if (shouldTrack)
                {
                    Windows.UI.Xaml.Media.CompositionTarget.Rendering += OnGraceRendering;
                }
                else
                {
                    Windows.UI.Xaml.Media.CompositionTarget.Rendering -= OnGraceRendering;
                }
            }

            void OnGraceRendering(object? _, object __)
            {
                UpdateDirectOutlineFor(comp4);
                UpdateGraceTracking();
            }

            void OnParallaxUpdatedForGrace(object? _, EventArgs __)
            {
                ExtendGrace();
                UpdateDirectOutlineFor(comp4);
                UpdateGraceTracking();
            }

            presenter.LayerParallaxUpdated += OnParallaxUpdatedForGrace;
            try
            {
                var dispatcher = Window.Current.Dispatcher;
                await RunFastSweepAsync(dispatcher, 100, 2.0, step =>
                {
                    presenter.SampleParallaxPoint(new Point(50 + step * 1100.0 / 100.0, 700 - step * 650.0 / 100.0));
                });
                await Task.Delay(350);
                Check(!tracking, "100ms trailing settle grace unhooks CompositionTarget.Rendering after layer parallax settles");
                await VerifyAlignmentAsync("Align_After2msSweepAndSettleGrace", comp4, maxDipError: 1.2, updateOutline: false);
            }
            finally
            {
                presenter.LayerParallaxUpdated -= OnParallaxUpdatedForGrace;
                Windows.UI.Xaml.Media.CompositionTarget.Rendering -= OnGraceRendering;
            }
        }
        finally
        {
            pageRoot.Children.Clear();
        }
    }

    private static async Task<int> RunFastSweepAsync(
        CoreDispatcher dispatcher,
        int totalSteps,
        double intervalMs,
        Action<int> onStep)
    {
        var lastDone = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = 0;

        await Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            for (var step = 1; step <= totalSteps; step++)
            {
                var targetMs = step * intervalMs;
                while (sw.Elapsed.TotalMilliseconds < targetMs)
                {
                    Thread.SpinWait(50);
                }

                var capturedStep = step;
                _ = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    onStep(capturedStep);
                    executed++;
                    if (capturedStep == totalSteps)
                    {
                        lastDone.TrySetResult(executed);
                    }
                });
            }

            await lastDone.Task.ConfigureAwait(false);
        });

        return await lastDone.Task;
    }

    private sealed record DwmSweepMetrics(
        int StopIndex,
        int DuringMove,
        double DuringDx,
        int PostStopMove,
        double PostStopDx);

    private async Task RunParallaxDwmRegressionAsync(string reportPath)
    {
        var uiSettings = new UISettings();
        _report.Add($"DWM parallax regression checks (UISettings.AnimationsEnabled={uiSettings.AnimationsEnabled})");
        File.WriteAllLines(reportPath, _report);

        static SceneSettings CreateDiagScene(double strength, bool includeLayer = true) => new()
        {
            Background = new BackgroundSettings
            {
                Mode = BackgroundMode.None,
                ParallaxStrength = strength
            },
            Layers = includeLayer
                ?
                [
                    new LayerSettings
                    {
                        Name = "DwmDiagLayer",
                        Kind = LayoutKind.Grid,
                        Features =
                        [
                            new FeatureInfo { Id = "omls.features.layout.parallax", Settings = new ParallaxFeatureSettings() }
                        ],
                        Children =
                        [
                            new ComponentSettings
                            {
                                Id = TextComponent.Id,
                                Settings = new TextComponentSettings { Text = "" },
                                Features =
                                [
                                    new FeatureInfo
                                    {
                                        Id = "omls.features.appearance.background",
                                        Settings = new BackgroundFeatureSettings { Background = new SolidColorBrush(Colors.White) }
                                    },
                                    new FeatureInfo
                                    {
                                        Id = "omls.features.layout.layout",
                                        Settings = new LayoutFeatureSettings
                                        {
                                            Width = 120,
                                            Height = 120,
                                            IsAutoWidth = false,
                                            IsAutoHeight = false,
                                            HorizontalAlignment = HorizontalAlignment.Center,
                                            VerticalAlignment = VerticalAlignment.Center
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                ]
                : []
        };

        var dispatcher = Window.Current.Dispatcher;

        // 1 & 2. LayerPresenter high-frequency sweeps (2ms & 6ms) and mid-sweep direction reversal + rapid strength scrub
        {
            var diagScene = CreateDiagScene(strength: 200);
            var diagRoot = new Grid
            {
                Background = new SolidColorBrush(Colors.Black),
                Width = 1024,
                Height = 768
            };
            var diagPresenter = new ScenePresenter
            {
                Width = 1024,
                Height = 768,
                Background = new SolidColorBrush(Colors.Black),
                Scene = diagScene,
                ParallaxInputSurface = diagRoot
            };
            diagRoot.Children.Add(diagPresenter);
            Window.Current.Content = diagRoot;
            Window.Current.Activate();
            await Task.Delay(350);

            foreach (var (label, steps, intervalMs) in new (string Label, int Steps, double IntervalMs)[]
            {
                ("Layer_2ms_500Hz", 100, 2.0),
                ("Layer_6ms_166Hz", 34, 6.0)
            })
            {
                diagPresenter.SampleParallaxPoint(new Point(0, 384));
                await Task.Delay(300);

                var (baseline, frames, sweepEndMs) = await CaptureContinuousFramesAsync(diagRoot, async markSweepEnd =>
                {
                    await RunFastSweepAsync(dispatcher, steps, intervalMs, step =>
                    {
                        diagPresenter.SampleParallaxPoint(new Point(step * 1024.0 / steps, 384));
                    });
                    markSweepEnd();
                    await Task.Delay(300);
                });

                var metrics = AnalyzeDwmSweep(baseline, frames, sweepEndMs);
                LogDwmProbeSeries($"{label} ({steps}@{intervalMs}ms)", baseline, frames, sweepEndMs, metrics);
                Check(metrics.DuringMove >= 5
                    && metrics.DuringDx >= 8.0
                    && metrics.PostStopMove >= 2
                    && metrics.PostStopDx >= 0.4
                    && Math.Abs((metrics.DuringDx + metrics.PostStopDx) - 16.42) < 2.0,
                    $"{label} must advance continuously during input and coast smoothly afterward " +
                    $"(duringMove={metrics.DuringMove}, duringDx={metrics.DuringDx:F2}, postStopMove={metrics.PostStopMove}, postStopDx={metrics.PostStopDx:F2})");
                File.WriteAllLines(reportPath, _report);
            }

            // Mid-sweep direction reversal at 2ms (0 -> 1024 in 150ms, then 1024 -> 0 in 150ms)
            diagPresenter.SampleParallaxPoint(new Point(0, 384));
            await Task.Delay(300);
            var (revBase, revFrames, revEndMs) = await CaptureContinuousFramesAsync(diagRoot, async markSweepEnd =>
            {
                await RunFastSweepAsync(dispatcher, 75, 2.0, step =>
                {
                    diagPresenter.SampleParallaxPoint(new Point(step * 1024.0 / 75, 384));
                });
                await RunFastSweepAsync(dispatcher, 75, 2.0, step =>
                {
                    diagPresenter.SampleParallaxPoint(new Point(1024.0 - step * 1024.0 / 75, 384));
                });
                markSweepEnd();
                await Task.Delay(300);
            });
            var revMetrics = AnalyzeDwmSweep(revBase, revFrames, revEndMs);
            LogDwmProbeSeries("Layer_Reversal_2ms", revBase, revFrames, revEndMs, revMetrics);
            var peakCx = revFrames.Count > 0 ? revFrames.Max(f => f.CenterX) : double.NaN;
            var endCx = revFrames.Count > 0 ? revFrames[^1].CenterX : double.NaN;
            Check(peakCx > revBase.CenterX + 8.0 && Math.Abs(endCx - revBase.CenterX) < 1.5,
                $"Layer_Reversal_2ms moves right on forward pass and returns cleanly on reverse " +
                $"(baseCx={revBase.CenterX:F2}, peakCx={peakCx:F2}, endCx={endCx:F2})");
            File.WriteAllLines(reportPath, _report);

            // Rapid 6ms ParallaxStrength scrub (0 -> 200 over 300ms)
            diagScene.Background.ParallaxStrength = 0;
            diagPresenter.SampleParallaxPoint(new Point(512, 384));
            await Task.Delay(200);
            var (scaleBase, scaleFrames, scaleEndMs) = await CaptureContinuousFramesAsync(diagRoot, async markSweepEnd =>
            {
                await RunFastSweepAsync(dispatcher, 50, 6.0, step =>
                {
                    diagScene.Background.ParallaxStrength = step * 200.0 / 50.0;
                });
                markSweepEnd();
                await Task.Delay(1100);
            });
            var scaleMetrics = AnalyzeDwmSweep(scaleBase, scaleFrames, scaleEndMs);
            var stopIdx = scaleMetrics.StopIndex >= 0 ? scaleMetrics.StopIndex : 0;
            var duringEffDelta = scaleFrames.Count > stopIdx ? scaleFrames[stopIdx].EffectiveSize - scaleBase.EffectiveSize : 0;
            var postStopEffDelta = scaleFrames.Count > stopIdx ? scaleFrames[^1].EffectiveSize - scaleFrames[stopIdx].EffectiveSize : 0;
            var finalEffDelta = scaleFrames.Count > 0 ? scaleFrames[^1].EffectiveSize - scaleBase.EffectiveSize : 0;
            LogDwmProbeSeries($"Layer_StrengthScrub_6ms (dEff={duringEffDelta:F2}+{postStopEffDelta:F2})", scaleBase, scaleFrames, scaleEndMs, scaleMetrics);
            Check(duringEffDelta >= 0.6 && postStopEffDelta >= 0.6 && finalEffDelta >= 3.5,
                $"Rapid 6ms ParallaxStrength scrub smoothly scales during and after scrub " +
                $"(duringEff={duringEffDelta:F2}, postEff={postStopEffDelta:F2}, finalEff={finalEffDelta:F2})");
            File.WriteAllLines(reportPath, _report);

            diagRoot.Children.Clear();
        }

        // 3. BackgroundParallaxHost high-frequency 2ms sweep
        {
            var bgScene = CreateDiagScene(strength: 200, includeLayer: false);
            var bgRoot = new Grid
            {
                Background = new SolidColorBrush(Colors.Black),
                Width = 1024,
                Height = 768
            };
            var bgPresenter = new ScenePresenter
            {
                Width = 1024,
                Height = 768,
                Background = new SolidColorBrush(Colors.Black),
                Scene = bgScene,
                ParallaxInputSurface = bgRoot
            };
            bgRoot.Children.Add(bgPresenter);
            Window.Current.Content = bgRoot;
            Window.Current.Activate();
            await Task.Delay(350);

            var bgHostGrid = (Grid)bgPresenter.FindName("BackgroundParallaxHost");
            bgHostGrid.Children.Add(new Border
            {
                Width = 120,
                Height = 120,
                Background = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            bgRoot.UpdateLayout();
            bgPresenter.SampleParallaxPoint(new Point(0, 384));
            await Task.Delay(300);

            var (bgBase, bgFrames, bgSweepEndMs) = await CaptureContinuousFramesAsync(bgRoot, async markSweepEnd =>
            {
                await RunFastSweepAsync(dispatcher, 100, 2.0, step =>
                {
                    bgPresenter.SampleParallaxPoint(new Point(step * 1024.0 / 100, 384));
                });
                markSweepEnd();
                await Task.Delay(300);
            });
            var bgMetrics = AnalyzeDwmSweep(bgBase, bgFrames, bgSweepEndMs);
            LogDwmProbeSeries("BackgroundParallaxHost_2ms_500Hz (100@2ms)", bgBase, bgFrames, bgSweepEndMs, bgMetrics);
            Check(bgMetrics.DuringMove >= 5
                && bgMetrics.DuringDx >= 8.0
                && bgMetrics.PostStopMove >= 2
                && bgMetrics.PostStopDx >= 0.4
                && Math.Abs((bgMetrics.DuringDx + bgMetrics.PostStopDx) - 16.42) < 2.0,
                $"BackgroundParallaxHost 2ms sweep moves continuously during input and coasts smoothly afterward " +
                $"(duringMove={bgMetrics.DuringMove}, duringDx={bgMetrics.DuringDx:F2}, postStopMove={bgMetrics.PostStopMove}, postStopDx={bgMetrics.PostStopDx:F2})");
            File.WriteAllLines(reportPath, _report);

            bgRoot.Children.Clear();
        }
    }

    private static async Task<(CapturedFrame Baseline, List<CapturedFrame> Frames, double? SweepEndMs)> CaptureContinuousFramesAsync(
        FrameworkElement element,
        Func<Action, Task> runTimelineAsync)
    {
        element.UpdateLayout();
        await Task.Delay(120);

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var item = GraphicsCaptureItem.CreateFromVisual(visual);
        var size = item.Size.Width > 0 && item.Size.Height > 0
            ? item.Size
            : new SizeInt32((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight));
        var device = CanvasDevice.GetSharedDevice();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            size);
        using var session = pool.CreateCaptureSession(item);

        var gate = new object();
        var baselineReady = new TaskCompletionSource<CapturedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = new List<CapturedFrame>(96);
        CapturedFrame? baseline = null;
        Stopwatch? sw = null;
        double baselineSysMs = 0;
        double? sweepEndMs = null;
        var triggered = false;
        var stopped = false;

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object _)
        {
            while (true)
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null)
                {
                    break;
                }

                bool isStopped;
                bool isTriggered;
                Stopwatch? activeSw;
                double baseSys;
                lock (gate)
                {
                    isStopped = stopped;
                    isTriggered = triggered;
                    activeSw = sw;
                    baseSys = baselineSysMs;
                }

                if (isStopped)
                {
                    continue;
                }

                using var bitmap = CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Surface);
                var pixels = bitmap.GetPixelBytes();
                var w = (int)bitmap.SizeInPixels.Width;
                var h = (int)bitmap.SizeInPixels.Height;
                var sysMs = frame.SystemRelativeTime.TotalMilliseconds;
                var (cx, cy, bw, bh, eff, px) = MeasureWhiteBox(pixels, w, h);

                lock (gate)
                {
                    if (stopped)
                    {
                        continue;
                    }

                    if (!triggered)
                    {
                        baseline = new CapturedFrame(0, 0, cx, cy, bw, bh, eff, px);
                        baselineSysMs = sysMs;
                        baselineReady.TrySetResult(baseline);
                    }
                    else if (isTriggered && activeSw != null)
                    {
                        frames.Add(new CapturedFrame(
                            activeSw.Elapsed.TotalMilliseconds,
                            sysMs - baseSys,
                            cx,
                            cy,
                            bw,
                            bh,
                            eff,
                            px));
                    }
                }
            }
        }

        pool.FrameArrived += OnFrameArrived;
        session.StartCapture();

        var initial = await baselineReady.Task.WaitAsync(TimeSpan.FromSeconds(3));
        lock (gate)
        {
            sw = Stopwatch.StartNew();
            triggered = true;
        }

        await runTimelineAsync(() =>
        {
            lock (gate)
            {
                sweepEndMs = sw?.Elapsed.TotalMilliseconds;
            }
        });
        await Task.Delay(60);

        lock (gate)
        {
            stopped = true;
            pool.FrameArrived -= OnFrameArrived;
            return (baseline ?? initial, new List<CapturedFrame>(frames), sweepEndMs);
        }
    }

    private static (double CenterX, double CenterY, int BoxWidth, int BoxHeight, double EffectiveSize, int WhitePixels) MeasureWhiteBox(
        byte[] pixels,
        int width,
        int height)
    {
        double sumLuma = 0;
        double sumX = 0;
        double sumY = 0;
        var minX = int.MaxValue;
        var maxX = int.MinValue;
        var minY = int.MaxValue;
        var maxY = int.MinValue;
        var whitePixels = 0;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var idx = rowOffset + x * 4;
                var b = pixels[idx];
                var g = pixels[idx + 1];
                var r = pixels[idx + 2];
                var luma = (r + g + b) / 3.0;
                if (luma <= 24)
                {
                    continue;
                }

                sumLuma += luma;
                sumX += x * luma;
                sumY += y * luma;

                if (luma > 128)
                {
                    whitePixels++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        var cx = sumLuma > 0 ? sumX / sumLuma : double.NaN;
        var cy = sumLuma > 0 ? sumY / sumLuma : double.NaN;
        var bw = maxX >= minX ? maxX - minX + 1 : 0;
        var bh = maxY >= minY ? maxY - minY + 1 : 0;
        var eff = sumLuma > 0 ? Math.Sqrt(sumLuma / 255.0) : 0;
        return (cx, cy, bw, bh, eff, whitePixels);
    }

    private static DwmSweepMetrics AnalyzeDwmSweep(
        CapturedFrame baseline,
        IReadOnlyList<CapturedFrame> frames,
        double? sweepEndMs)
    {
        var duringSweepMove = 0;
        double duringSweepDx = 0;
        var postStopMove = 0;
        double postStopDx = 0;
        var stopIdx = -1;

        if (frames.Count >= 1)
        {
            if (sweepEndMs is { } endMs)
            {
                for (var i = 0; i < frames.Count; i++)
                {
                    var prevCx = i == 0 ? baseline.CenterX : frames[i - 1].CenterX;
                    var moved = Math.Abs(frames[i].CenterX - prevCx) > 0.05;
                    if (frames[i].ElapsedMs <= endMs)
                    {
                        stopIdx = i;
                        if (moved) duringSweepMove++;
                    }
                    else
                    {
                        if (moved) postStopMove++;
                    }
                }

                var atStop = stopIdx >= 0 ? frames[stopIdx] : baseline;
                duringSweepDx = atStop.CenterX - baseline.CenterX;
                postStopDx = frames[^1].CenterX - atStop.CenterX;
            }
            else
            {
                duringSweepDx = frames[^1].CenterX - baseline.CenterX;
            }
        }

        return new DwmSweepMetrics(stopIdx, duringSweepMove, duringSweepDx, postStopMove, postStopDx);
    }

    private void LogDwmProbeSeries(
        string name,
        CapturedFrame baseline,
        IReadOnlyList<CapturedFrame> frames,
        double? sweepEndMs,
        DwmSweepMetrics metrics)
    {
        var sweepSuffix = sweepEndMs is { } mark
            ? $", sweepEnd={mark:F1}ms, duringMove={metrics.DuringMove}, duringDx={metrics.DuringDx:F2}, postStopMove={metrics.PostStopMove}, postStopDx={metrics.PostStopDx:F2}"
            : $", dx={metrics.DuringDx:F2}";
        _report.Add($"DIAG [{name}]: frames={frames.Count}{sweepSuffix} | base(cx={baseline.CenterX:F2}, w={baseline.BoxWidth}, eff={baseline.EffectiveSize:F2})");

        if (frames.Count == 0)
        {
            return;
        }

        var indices = new SortedSet<int>();
        for (var i = 0; i < Math.Min(4, frames.Count); i++) indices.Add(i);
        if (metrics.StopIndex >= 0) indices.Add(metrics.StopIndex);
        var afterStart = Math.Max(0, metrics.StopIndex + 1);
        for (var i = afterStart; i < Math.Min(frames.Count, afterStart + 3); i++) indices.Add(i);
        indices.Add(frames.Count - 1);

        foreach (var i in indices)
        {
            if (indices.Count > 10 && i != indices.Max && i > 2 && i < afterStart) continue;
            var frame = frames[i];
            _report.Add($"  [{i,2}] t={frame.ElapsedMs,6:F1}ms sys={frame.SysRelativeMs,6:F1}ms cx={frame.CenterX,7:F2} w={frame.BoxWidth,3} eff={frame.EffectiveSize,6:F2}");
        }
    }

    private static T? FindFirst<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            if (FindFirst<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
