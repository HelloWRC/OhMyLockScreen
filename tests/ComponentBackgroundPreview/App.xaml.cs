using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Services.Features.Layout;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using Windows.ApplicationModel.Activation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace ComponentBackgroundPreview;

public sealed partial class App : Application
{
    private readonly TextBlock _status = new() { Text = "正在验证原生组件背景与边框…", Margin = new Thickness(16) };
    private ComponentPresenter _presenter = null!;
    private readonly List<string> _report = [];

    public App()
    {
        UnhandledException += (_, args) =>
        {
            args.Handled = true;
            File.WriteAllText(Path.Combine(ApplicationData.Current.LocalFolder.Path, "report.txt"), "FAIL: " + args.Exception);
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (args.Arguments.Contains("spectrum", StringComparison.OrdinalIgnoreCase))
        {
            LaunchSpectrumPreview();
            return;
        }

        if (args.Arguments.Contains("system-status", StringComparison.OrdinalIgnoreCase))
        {
            LaunchSystemStatusPreview();
            return;
        }

        if (args.Arguments.Contains("media", StringComparison.OrdinalIgnoreCase))
        {
            LaunchMediaPreview();
            return;
        }

        // This package has separate app data and no lock-screen extension or settings services.
        IAppHost.Host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddSingleton<IFeatureService, FeatureService>();
            services.AddSingleton<IComponentsService, ComponentsService>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<FontCatalogService>();
            services.AddFeature<BorderFeature, BorderSettingsControl>();
            services.AddFeature<TextFeature, TextFeatureSettingsControl>();
            services.AddFeature<FontFeature, FontSettingsControl>();
            services.AddFeature<ForegroundFeature, ForegroundSettingsControl>();
            services.AddFeature<LayoutFeature>();
            services.AddFeature<GridLayoutFeature>();
            services.AddComponent<TextComponent>();
            services.AddComponent<ClockComponent>();
            services.AddComponent<DateComponent>();
            services.AddComponent<ContainerComponent>();
        }).Build();
        _presenter = new ComponentPresenter { Width = 240, Height = 120 };
        var root = new StackPanel { Background = new SolidColorBrush(Colors.Black), Spacing = 16 };
        root.Children.Add(_status);
        root.Children.Add(_presenter);
        root.Loaded += async (_, _) => await VerifyAsync(root);
        Window.Current.Content = root;
        Window.Current.Activate();
    }

    private async Task VerifyAsync(StackPanel root)
    {
        try
        {
            await Task.Delay(100);
            var content = new UserControl { Content = new TextBlock { Text = "组件内容", Foreground = new SolidColorBrush(Colors.White) } };
            _presenter.Content = content;
            _presenter.UpdateLayout();
            await Task.Delay(100);
            Check(_presenter.Border != null, "IBorderable exposes the realized template border");
            var border = _presenter.Border!;
            border.Background = new SolidColorBrush(Colors.Red);
            await ExpectPixelAsync(Colors.Red, "Features can paint the decoration border directly");
            Check(ReferenceEquals(_presenter.Content, content), "The wrapper preserves the original component instance");

            border.Background = new SolidColorBrush(Colors.Blue);
            await ExpectPixelAsync(Colors.Blue, "Replacing the background updates the rendered pixels");

            border.Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0.5),
                EndPoint = new Windows.Foundation.Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop { Color = Colors.Red, Offset = 0 },
                    new GradientStop { Color = Colors.Blue, Offset = 1 }
                }
            };
            var gradient = await RenderAsync();
            var left = Pixel(gradient, 2);
            var right = Pixel(gradient, gradient.Width - 3);
            Check(left.A == 255 && right.A == 255 && left.R > right.R && right.B > left.B,
                "Linear gradients are painted across the wrapper bounds");
            Check(Math.Abs(content.ActualWidth - _presenter.ActualWidth) < 1
                && Math.Abs(content.ActualHeight - _presenter.ActualHeight) < 1,
                "Component and background bounds match for the gradient adorner");

            border.Background = null;
            await ExpectPixelAsync(Colors.Transparent, "Clearing the background clears the rendered pixels");

            var replacement = new UserControl { Content = new TextBlock { Text = "替换的组件" } };
            _presenter.Content = replacement;
            _presenter.UpdateLayout();
            await Task.Delay(100);
            _presenter.Border!.Background = new SolidColorBrush(Colors.Green);
            await ExpectPixelAsync(Colors.Green, "Replacing content retains an accessible decoration border");
            content.Background = new SolidColorBrush(Colors.Red);
            await ExpectPixelAsync(Colors.Green, "The old component no longer controls the wrapper");

            var otherPresenter = new ComponentPresenter { Width = 160, Height = 80 };
            root.Children.Add(otherPresenter);
            await Task.Delay(100);
            otherPresenter.Content = new UserControl();
            otherPresenter.UpdateLayout();
            await Task.Delay(100);
            otherPresenter.Border!.Background = new SolidColorBrush(Colors.Yellow);
            var otherImage = await RenderAsync(otherPresenter);
            var otherPixel = Pixel(otherImage, otherImage.Width - 3);
            Check(otherPixel.A == 255 && otherPixel.R == 255 && otherPixel.G == 255 && otherPixel.B == 0,
                "Another presenter exposes its own independent decoration border");
            await ExpectPixelAsync(Colors.Green, "Multiple presenters keep independent background bindings");
            root.Children.Remove(otherPresenter);

            root.Children.Remove(_presenter);
            await Task.Delay(100);
            Check(_presenter.Border == null, "IBorderable clears the border when its host unloads");
            root.Children.Add(_presenter);
            // Loaded recreates content as it does in the app; provide a fresh component afterwards.
            await Task.Delay(100);
            _presenter.Content = replacement;
            _presenter.UpdateLayout();
            await Task.Delay(100);
            Check(_presenter.Border != null, "IBorderable exposes the reloaded decoration border");
            _presenter.Border!.Background = new SolidColorBrush(Colors.Green);
            await ExpectPixelAsync(Colors.Green, "The default template still paints after unload and reload");
            await VerifyBorderAsync(root);
            await VerifyTypographyAsync(root);
            await VerifyDropPositionAsync(root);
            await VerifyRealizedGridDropAsync(root);
            await VerifyReorderedForegroundAsync(root);
        }
        catch (Exception exception)
        {
            _report.Add("FAIL: " + exception);
        }

        _status.Text = _report.Any(line => line.StartsWith("FAIL:")) ? "FAIL" : $"PASS: {_report.Count} native rendering checks";
        var reportFile = await ApplicationData.Current.LocalFolder.CreateFileAsync("report.txt", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(reportFile, string.Join(Environment.NewLine, _report));
    }

    private async Task VerifyBorderAsync(StackPanel root)
    {
        var service = IAppHost.GetRequiredService<IFeatureService>();
        var info = new FeatureInfo { Id = "omls.features.appearance.border" };
        using var feature = (BorderFeature)service.CreateFeature(info)!;
        var settings = feature.Settings;
        var border = _presenter.Border!;
        Check(Resources.TryGetValue("SystemControlForegroundBaseHighBrush", out var resource)
            && resource is SolidColorBrush themeBrush && settings.BorderBrush is SolidColorBrush copiedBrush
            && copiedBrush.Color.Equals(themeBrush.Color) && !ReferenceEquals(copiedBrush, themeBrush),
            "The default outline copies the native theme foreground color");
        feature.Attach(_presenter);
        Check(border.BorderThickness.Equals(new Thickness(1)), "The native default border is one unit thick");

        var editor = (BorderSettingsControl)service.CreateSettingsControl(info)!;
        editor.Width = 325;
        editor.TargetElement = border;
        var scroll = new ScrollViewer { Content = editor, MaxHeight = 680 };
        root.Children.Add(scroll);
        root.UpdateLayout();
        await Task.Delay(150);

        var inputs = Descendants(editor).OfType<Microsoft.UI.Xaml.Controls.NumberBox>()
            .Where(input => AutomationProperties.GetName(input) is var name
                && (name.EndsWith("框线粗细") || name.EndsWith("圆角半径") || name.EndsWith("内边距")))
            .ToDictionary(AutomationProperties.GetName);
        Check(inputs.Count == 12, "The native editor presents all twelve named geometry inputs");
        string[] names = ["左框线粗细", "上框线粗细", "右框线粗细", "下框线粗细",
            "左上圆角半径", "右上圆角半径", "右下圆角半径", "左下圆角半径",
            "左内边距", "上内边距", "右内边距", "下内边距"];
        for (var index = 0; index < names.Length; index++)
        {
            inputs[names[index]].Value = index + 2.5;
        }

        Check(border.BorderThickness.Equals(new Thickness(2.5, 3.5, 4.5, 5.5))
            && border.CornerRadius.Equals(new CornerRadius(6.5, 7.5, 8.5, 9.5))
            && border.Padding.Equals(new Thickness(10.5, 11.5, 12.5, 13.5)),
            "All native NumberBox bindings preserve asymmetric border geometry");
        settings.BorderThickness = new Thickness(10, 11, 12, 13);
        Check(inputs["左框线粗细"].Value == 10 && inputs["下框线粗细"].Value == 13,
            "External geometry changes refresh the native editor fields");
        inputs["左框线粗细"].Value = double.NaN;
        Check(settings.BorderThickness.Left == 10, "Clearing a native NumberBox preserves the saved value");
        inputs["左框线粗细"].Value = 10;

        settings.BorderBrush = new SolidColorBrush(Colors.Yellow);
        var image = await RenderAsync();
        var edge = PixelAt(image, 1, image.Height / 2);
        var corner = PixelAt(image, 0, 0);
        Check(edge.A == 255 && edge.R == 255 && edge.G == 255 && edge.B == 0,
            "A solid feature brush paints the native border pixels");
        Check(corner.A == 0, "The native asymmetric corner radius rounds the outer corner");
        settings.BorderBrush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0.5),
            EndPoint = new Windows.Foundation.Point(1, 0.5),
            GradientStops =
            {
                new GradientStop { Color = Colors.Red, Offset = 0 },
                new GradientStop { Color = Colors.Blue, Offset = 1 }
            }
        };
        image = await RenderAsync();
        var left = PixelAt(image, 1, image.Height / 2);
        var right = PixelAt(image, image.Width - 2, image.Height / 2);
        Check(left.R > right.R && right.B > left.B, "The feature paints a native gradient across the outline");
        var brushEditor = Descendants(editor).OfType<OhMyLockScreen.Shared.Controls.UI.BrushEditControl>().Single();
        var replacementBrush = new SolidColorBrush(Colors.Yellow);
        brushEditor.Brush = replacementBrush;
        Check(ReferenceEquals(settings.BorderBrush, replacementBrush) && ReferenceEquals(border.BorderBrush, replacementBrush),
            "The native brush editor writes its replacement brush through the feature settings");
        await CaptureAsync(root, "border-editor.png");
        scroll.ChangeView(null, scroll.ScrollableHeight, null, disableAnimation: true);
        await Task.Delay(100);
        await CaptureAsync(root, "border-geometry.png");

        settings.BorderBrush = null;
        image = await RenderAsync();
        Check(PixelAt(image, 1, image.Height / 2).A == 0, "Clearing the feature brush clears the native outline");
        root.Children.Remove(scroll);
        editor.TargetElement = null;
        await Task.Delay(100);
        settings.Padding = new Thickness(20);
        root.Children.Add(scroll);
        await Task.Delay(100);
        Check(inputs["左内边距"].Value == 20, "The native settings editor resumes notifications after reloading");

        root.Children.Remove(_presenter);
        await Task.Delay(100);
        Check(border.BorderBrush == null && border.BorderThickness.Equals(default(Thickness))
            && border.CornerRadius.Equals(default(CornerRadius)) && border.Padding.Equals(default(Thickness)),
            "Unloading the native presenter restores all original border values");
        root.Children.Insert(1, _presenter);
        await Task.Delay(100);
        _presenter.Content = new UserControl { Content = new TextBlock { Text = "边框预览" } };
        _presenter.UpdateLayout();
        await Task.Delay(100);
        Check(_presenter.Border != null && _presenter.Border.Padding.Equals(new Thickness(20)),
            "A recreated native border receives the latest feature settings");
        feature.Detach();
        Check(_presenter.Border!.Padding.Equals(default(Thickness)), "Detaching the feature restores the recreated border");
        root.Children.Remove(scroll);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static Color PixelAt((byte[] Pixels, int Width, int Height) image, int x, int y)
    {
        var offset = (y * image.Width + x) * 4;
        return Color.FromArgb(image.Pixels[offset + 3], image.Pixels[offset + 2], image.Pixels[offset + 1], image.Pixels[offset]);
    }

    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }

    private async Task ExpectPixelAsync(Color expected, string description)
    {
        var image = await RenderAsync();
        var actual = Pixel(image, image.Width - 3);
        Check(actual.A == expected.A && (expected.A == 0 || actual.R == expected.R && actual.G == expected.G && actual.B == expected.B),
            $"{description} (actual ARGB={actual.A},{actual.R},{actual.G},{actual.B})");
    }

    private async Task<(byte[] Pixels, int Width, int Height)> RenderAsync(ComponentPresenter? presenter = null)
    {
        var target = presenter ?? _presenter;
        target.UpdateLayout();
        await Task.Delay(100);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(target);
        return ((await bitmap.GetPixelsAsync()).ToArray(), bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private static Color Pixel((byte[] Pixels, int Width, int Height) image, int x)
    {
        var offset = ((image.Height - 3) * image.Width + x) * 4;
        return Color.FromArgb(image.Pixels[offset + 3], image.Pixels[offset + 2], image.Pixels[offset + 1], image.Pixels[offset]);
    }

    private void Check(bool condition, string description) => _report.Add((condition ? "PASS: " : "FAIL: ") + description);
}
