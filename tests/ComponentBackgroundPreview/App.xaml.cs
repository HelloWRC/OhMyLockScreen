using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace ComponentBackgroundPreview;

public sealed partial class App : Application
{
    private readonly TextBlock _status = new() { Text = "正在验证原生组件背景…", Margin = new Thickness(16) };
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
        // This package has separate app data and no lock-screen extension or settings services.
        IAppHost.Host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddSingleton<IFeatureService, EmptyFeatureService>();
            services.AddSingleton(new FeatureRegistryService([]));
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
        }
        catch (Exception exception)
        {
            _report.Add("FAIL: " + exception);
        }

        _status.Text = _report.Any(line => line.StartsWith("FAIL:")) ? "FAIL" : $"PASS: {_report.Count} native rendering checks";
        var reportFile = await ApplicationData.Current.LocalFolder.CreateFileAsync("report.txt", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(reportFile, string.Join(Environment.NewLine, _report));
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

    private sealed class EmptyFeatureService : IFeatureService
    {
        public FeatureBase? CreateFeature(FeatureInfo settings) => null;
        public FeatureSettingsControlBase? CreateSettingsControl(FeatureInfo settings) => null;
    }
}
