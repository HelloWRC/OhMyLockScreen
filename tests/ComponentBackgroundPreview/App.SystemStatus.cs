using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Services.SystemStatus;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.SystemStatus;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Models.SystemStatus;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async void LaunchSystemStatusPreview()
    {
        try
        {
            var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<ISystemStatusSource>(_ => new WindowsSystemStatusSource(new StatusPreviewLogger(errors)));
                services.AddSingleton<ISystemStatusService, SystemStatusService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddFeature<TextFeature>();
                services.AddComponent<SystemStatusComponent, SystemStatusSettingsControl>();
            }).Build();
            var service = IAppHost.GetRequiredService<ISystemStatusService>();
            var components = IAppHost.GetRequiredService<IComponentsService>();
            var panel = new StackPanel
            {
                Width = 470,
                Padding = new Thickness(24),
                Spacing = 16,
                Background = new SolidColorBrush(Colors.Black)
            };
            panel.Children.Add(new TextBlock { Text = "系统状态 · 原生预览", FontSize = 26, Foreground = new SolidColorBrush(Colors.White) });
            var views = new List<SystemStatusComponent>();
            var notifications = new System.Collections.Concurrent.ConcurrentDictionary<SystemStatusKind, int>();
            service.Changed += (_, args) => notifications.AddOrUpdate(args.Kind, 1, (_, count) => count + 1);
            foreach (var kind in Enum.GetValues<SystemStatusKind>())
            {
                panel.Children.Add(new TextBlock { Text = kind.ToString(), FontSize = 14, Foreground = new SolidColorBrush(Colors.Gray) });
                var view = (SystemStatusComponent)components.CreateComponent(new ComponentSettings
                {
                    Id = SystemStatusComponent.Id,
                    Settings = new SystemStatusComponentSettings { Kind = kind, ShowText = true }
                })!;
                view.FontSize = 16;
                view.Foreground = new SolidColorBrush(Colors.White);
                views.Add(view);
                panel.Children.Add(view);
            }

            var options = components.CreateSettingsControl(new ComponentSettings
            {
                Id = SystemStatusComponent.Id,
                Settings = views[0].Settings
            })!;
            panel.Children.Add(options);
            Window.Current.Content = panel;
            Window.Current.Activate();
            await Task.Delay(2500);
            foreach (var kind in Enum.GetValues<SystemStatusKind>())
            {
                var snapshot = service.GetState(kind);
                Check(snapshot.Availability != SystemStatusAvailability.Unavailable,
                    $"Native {kind}: {snapshot.Availability}, {SystemStatusPresentation.Create(kind, snapshot).Text}"
                    + (kind == SystemStatusKind.Network ? $", type {snapshot.NetworkKind}" : ""));
            }

            await VerifyStatusIconsAsync(panel);
            await VerifyInheritedStatusFontSizeAsync(panel);
            var volumeReads = new System.Collections.Concurrent.ConcurrentQueue<SystemStatusSnapshot>();
            var volume = new AudioVolumeObserver(volumeReads.Enqueue, exception => errors.Enqueue("ERROR: " + exception));
            await Task.Delay(300);
            var initialReads = volumeReads.Count;
            new AudioVolumeObserver.VolumeCallback(volume, 1).OnNotify(0);
            await Task.Delay(150);
            Check(initialReads > 0 && volumeReads.Count > initialReads,
                "Audio volume notification schedules a fresh native endpoint read");
            volume.Dispose();
            var stoppedReads = volumeReads.Count;
            new AudioVolumeObserver.VolumeCallback(volume, 1).OnNotify(0);
            await Task.Delay(100);
            Check(volumeReads.Count == stoppedReads, "Late native volume callbacks cannot read a released endpoint");

            var battery = views[0];
            var content = (Grid)battery.Content;
            var icon = (FluentIcons.Uwp.FluentIcon)content.Children[0];
            var text = (TextBlock)content.Children[1];
            battery.FontSize = 32;
            await Task.Delay(60);
            Check(Math.Abs(icon.FontSize - 24) < 0.1 && Math.Abs(text.FontSize - 32) < 0.1,
                $"Text font size changes leave icon size independent: {icon.FontSize}, {text.FontSize}");
            var sizeInput = ((StackPanel)options.Content).Children.OfType<Microsoft.UI.Xaml.Controls.NumberBox>().Single();
            Check(sizeInput.Value == 24, "Icon size editor initially displays the default 24");
            sizeInput.Value = 40;
            await Task.Delay(60);
            Check(battery.Settings.IconSize == 40 && icon.FontSize == 40 && text.FontSize == 32,
                "Icon size editor updates the icon while preserving text size");
            sizeInput.Value = double.NaN;
            await Task.Delay(60);
            Check(battery.Settings.IconSize == 40, "Clearing icon size input does not save NaN");
            sizeInput.Value = 24;
            await Task.Delay(60);
            var iconFamily = icon.FontFamily.Source;
            battery.FontFamily = new FontFamily("Consolas");
            await Task.Delay(60);
            Check(text.FontFamily.Source == "Consolas" && icon.FontFamily.Source == iconFamily,
                "Text font changes preserve the Fluent icon font");
            battery.Foreground = new SolidColorBrush(Colors.Red);
            await Task.Delay(60);
            Check(icon.Foreground is SolidColorBrush { Color: var iconColor } && iconColor == Colors.Red
                && text.Foreground is SolidColorBrush { Color: var textColor } && textColor == Colors.Red,
                "Icon and text inherit the component foreground");
            battery.Foreground = new SolidColorBrush(Colors.White);
            foreach (var showIcon in new[] { true, false })
            foreach (var showText in new[] { true, false })
            {
                battery.Settings.ShowIcon = showIcon;
                battery.Settings.ShowText = showText;
                await Task.Delay(30);
                Check(icon.Visibility == (showIcon ? Visibility.Visible : Visibility.Collapsed)
                    && text.Visibility == (showText ? Visibility.Visible : Visibility.Collapsed)
                    && content.ColumnSpacing == (showIcon && showText ? 6 : 0),
                    $"Native visibility combination icon={showIcon}, text={showText}");
            }

            battery.Settings.ShowIcon = true;
            battery.Settings.ShowText = true;
            battery.FontSize = 16;
            battery.ClearValue(Control.FontFamilyProperty);
            views[1].Width = 90;
            views[1].HorizontalAlignment = HorizontalAlignment.Left;
            await Task.Delay(60);
            await CaptureAsync(panel, "system-status.png");
            // Leave the isolated preview active for manual changes to volume, network and Bluetooth.
            await Task.Delay(3000);
            foreach (var kind in Enum.GetValues<SystemStatusKind>())
            {
                _report.Add($"INFO: {kind} changes observed: {notifications.GetValueOrDefault(kind)}");
            }

            var lastView = views[3];
            panel.Children.Remove(lastView);
            await Task.Delay(60);
            Check(service.GetState(SystemStatusKind.Bluetooth) == SystemStatusSnapshot.Unavailable,
                "Removing the final Bluetooth view releases its native observers");
            panel.Children.Add(lastView);
            await Task.Delay(1000);
            Check(service.GetState(SystemStatusKind.Bluetooth).Availability != SystemStatusAvailability.Unavailable,
                "Reloading a view starts fresh native observers");
            _report.Add("INFO: Interactive preview remains active for manual device changes.");
            _report.AddRange(errors);
        }
        catch (Exception exception)
        {
            _report.Add("FAIL: " + exception);
        }

        var report = await ApplicationData.Current.LocalFolder.CreateFileAsync("system-status-report.txt", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteLinesAsync(report, _report);
    }

    private async Task VerifyInheritedStatusFontSizeAsync(StackPanel panel)
    {
        var typography = new TextFeatureSettings { FontSize = 12 };
        var presenter = new ComponentPresenter
        {
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Left,
            Settings = new ComponentSettings
            {
                Id = SystemStatusComponent.Id,
                Settings = new SystemStatusComponentSettings { ShowText = true },
                Features = [new FeatureInfo { Id = TextFeature.Id, Settings = typography }]
            }
        };
        panel.Children.Insert(1, presenter);
        await Task.Delay(100);
        var component = (SystemStatusComponent)presenter.Content;
        var icon = (FluentIcons.Uwp.FluentIcon)((Grid)component.Content).Children[0];
        var text = (TextBlock)component.TextElement;
        var heights = new List<int>();
        foreach (var size in new[] { 12d, 48d, 20d })
        {
            typography.FontSize = size;
            presenter.UpdateLayout();
            await Task.Delay(100);
            Check(Math.Abs(text.FontSize - size) < 0.1 && Math.Abs(icon.FontSize - 24) < 0.1,
                $"Text feature size {size} reaches text and preserves icon size: {text.FontSize}, {icon.FontSize}");
            heights.Add(await PaintedIconHeightAsync());
        }

        Check(heights[0] > 0 && heights.All(height => height == heights[0]),
            $"Changing text size leaves actual icon pixels unchanged: {string.Join(", ", heights)}");
        heights.Clear();
        foreach (var size in new[] { 12d, 48d, 20d })
        {
            component.Settings.IconSize = size;
            presenter.UpdateLayout();
            await Task.Delay(100);
            Check(Math.Abs(icon.FontSize - size) < 0.1 && Math.Abs(text.FontSize - 20) < 0.1,
                $"Icon size {size} preserves the text feature size: {icon.FontSize}, {text.FontSize}");
            heights.Add(await PaintedIconHeightAsync());
        }

        Check(heights[0] > 0 && heights[1] >= heights[0] * 2.5 && heights[2] < heights[1],
            $"Changing icon size resizes actual icon pixels in both directions: {string.Join(", ", heights)}");
        panel.Children.Remove(presenter);

        async Task<int> PaintedIconHeightAsync()
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(icon);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            var rows = Enumerable.Range(0, bitmap.PixelHeight).Where(y => Enumerable.Range(0, bitmap.PixelWidth)
                .Any(x => pixels[(y * bitmap.PixelWidth + x) * 4 + 3] > 20)).ToArray();
            return rows.Length == 0 ? 0 : rows[^1] - rows[0] + 1;
        }
    }

    private async Task VerifyStatusIconsAsync(StackPanel panel)
    {
        var icons = new HashSet<FluentIcons.Common.Icon>();
        var available = new SystemStatusSnapshot { Availability = SystemStatusAvailability.Available };
        for (var percent = 0; percent <= 100; percent += 10)
        {
            icons.Add(SystemStatusPresentation.Create(SystemStatusKind.Battery, available with { Percent = percent }).Icon);
            icons.Add(SystemStatusPresentation.Create(SystemStatusKind.Battery, available with { Percent = percent, IsCharging = true }).Icon);
        }

        foreach (var kind in Enum.GetValues<SystemStatusKind>())
        foreach (var availability in Enum.GetValues<SystemStatusAvailability>())
        {
            icons.Add(SystemStatusPresentation.Create(kind, available with { Availability = availability }).Icon);
        }

        foreach (var kind in Enum.GetValues<SystemNetworkKind>())
        {
            icons.Add(SystemStatusPresentation.Create(SystemStatusKind.Network, available with { NetworkKind = kind }).Icon);
        }

        foreach (var percent in new[] { 0, 20, 80 })
        {
            icons.Add(SystemStatusPresentation.Create(SystemStatusKind.Volume, available with { Percent = percent }).Icon);
        }

        icons.Add(FluentIcons.Common.Icon.BluetoothConnected);
        var gallery = new Grid { Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Left };
        var icon = new FluentIcons.Uwp.FluentIcon
        {
            IconSize = FluentIcons.Common.IconSize.Resizable,
            FontSize = 24,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        gallery.Children.Add(icon);
        panel.Children.Insert(1, gallery);
        foreach (var value in icons)
        {
            icon.Icon = value;
            gallery.UpdateLayout();
            await Task.Delay(20);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(gallery);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            var painted = Enumerable.Range(0, pixels.Length / 4).Count(index => pixels[index * 4 + 3] > 20);
            Check(painted > 3, $"Native status icon {value} renders ({painted} pixels, glyph {icon.Glyph})");
        }

        panel.Children.Remove(gallery);
    }

    private sealed class StatusPreviewLogger(System.Collections.Concurrent.ConcurrentQueue<string> errors) : ILogger<WindowsSystemStatusSource>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            errors.Enqueue("ERROR: " + formatter(state, exception) + ": " + exception);
        }
    }
}
