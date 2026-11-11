using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.Audio;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Audio;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Models.Audio;
using OhMyLockScreen.Shared.Models.Media;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Services.Scene;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using Windows.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using System.Runtime.InteropServices.WindowsRuntime;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async void LaunchSpectrumPreview()
    {
        try
        {
            var source = new PreviewSpectrumService();
            var media = new SpectrumMediaState();
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IAudioSpectrumService>(source);
                services.AddSingleton<IMediaStateService>(media);
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddComponent<AudioSpectrumComponent, AudioSpectrumSettingsControl>();
            }).Build();
            var components = IAppHost.GetRequiredService<IComponentsService>();
            var root = new StackPanel { Spacing = 12, Padding = new Thickness(20), Background = new SolidColorBrush(Colors.Black) };
            root.Children.Add(new TextBlock { Text = "WASAPI 音频频谱 · 原生预览", FontSize = 24, Foreground = new SolidColorBrush(Colors.White) });
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var index = 0; index < 2; ++index)
            {
                grid.RowDefinitions.Add(new RowDefinition());
            }
            var views = new List<AudioSpectrumComponent>();
            var gradient = new LinearGradientBrush
            {
                StartPoint = new(0, 0),
                EndPoint = new(1, 1),
                GradientStops = { new GradientStop { Color = Colors.Cyan }, new GradientStop { Color = Colors.Magenta, Offset = 1 } }
            };
            foreach (var dock in Enum.GetValues<SpectrumDock>())
            {
                var view = (AudioSpectrumComponent)components.CreateComponent(new ComponentSettings
                {
                    Id = AudioSpectrumComponent.Id,
                    Settings = new AudioSpectrumComponentSettings { Dock = dock, ColorSource = SpectrumColorSource.Foreground }
                })!;
                view.Width = 320;
                view.Height = 120;
                view.Foreground = gradient;
                view.Margin = new Thickness(8);
                Grid.SetColumn(view, (int)dock % 2);
                Grid.SetRow(view, (int)dock / 2);
                grid.Children.Add(view);
                views.Add(view);
            }

            root.Children.Add(grid);
            root.Children.Add(_status);
            Window.Current.Content = new ScrollViewer { Content = root };
            Window.Current.Activate();
            await Task.Delay(500);
            Check(source.Users == 4, "All four views hold independent leases to the shared source");
            foreach (var view in views)
            {
                var paint = (Rectangle)view.Content;
                Check(ReferenceEquals(paint.Fill, gradient), $"{view.Settings.Dock} uses the full native gradient brush");
                Check(ElementCompositionPreview.GetElementVisual(paint).Clip is CompositionGeometricClip
                { Geometry: CompositionPathGeometry { Path: not null } }, $"{view.Settings.Dock} owns a live geometric clip");
                await VerifySpectrumPixelsAsync(view);
            }

            await CaptureAsync(grid, "spectrum.png");

            var target = views[0];
            foreach (var brush in new Brush[]
            {
                new Microsoft.UI.Xaml.Media.RadialGradientBrush
                {
                    GradientStops = { new GradientStop { Color = Colors.Red }, new GradientStop { Color = Colors.Blue, Offset = 1 } }
                },
                new AcrylicBrush { TintColor = Colors.Red, FallbackColor = Colors.Red, AlwaysUseFallback = true },
                new ImageBrush { ImageSource = new Windows.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/StoreLogo.png")) }
            })
            {
                target.Foreground = brush;
                await Task.Delay(80);
                Check(ReferenceEquals(((Rectangle)target.Content).Fill, brush), $"Native {brush.GetType().Name} is preserved");
            }

            target.Foreground = gradient;
            media.Current = new MediaState { HasSession = true, Cover = new byte[] { 1 }, ThemeColor = 0xFF23A5E7 };
            target.Settings.ColorSource = SpectrumColorSource.MediaCover;
            await Task.Delay(100);
            Check(((Rectangle)target.Content).Fill is SolidColorBrush { Color: var color } && color == Color.FromArgb(255, 35, 165, 231),
                "Cover mode uses the raw theme color without dimming");
            media.Current = MediaState.Empty;
            await Task.Delay(100);
            Check(ReferenceEquals(((Rectangle)target.Content).Fill, gradient), "Missing artwork falls back to foreground");
            var editor = components.CreateSettingsControl(new ComponentSettings { Id = AudioSpectrumComponent.Id, Settings = target.Settings });
            Check(editor is AudioSpectrumSettingsControl, "Component settings control resolves");
            root.Children.Add(editor!);
            for (var repeat = 0; repeat < 3; ++repeat)
            {
                grid.Children.Remove(target);
                await WaitForMediaThreadingAsync(() => source.Users == 3, "Spectrum unload releases its lease");
                Check(source.Users == 3, "Unload releases only the removed view's lease");
                grid.Children.Add(target);
                await WaitForMediaThreadingAsync(() => source.Users == 4, "Spectrum reload acquires its lease");
                Check(source.Users == 4, "Reload reacquires its lease and rendering resources");
            }

            // Notifications may originate on a worker with no synchronization context.
            await Task.Run(() => media.Current = new MediaState { HasSession = true, Cover = new byte[] { 1 }, ThemeColor = 0xFF23A5E7 });
            await Task.Delay(100);
            Check(((Rectangle)target.Content).Fill is SolidColorBrush { Color: var workerColor }
                && workerColor == Color.FromArgb(255, 35, 165, 231), "Worker-thread media updates return to the owning Dispatcher");
            await VerifySpectrumSecondViewAsync(source, media, components);

            source.Silent = true;
            await Task.Delay(1800);
            Check(await CountSpectrumPixelsAsync(target) == 0, "Silent real output has no visible columns");
            target.IsEditing = true;
            await Task.Delay(100);
            Check(await CountSpectrumPixelsAsync(target) > 0, "Only editing mode supplies static columns during silence");
            await CaptureAsync(target, "spectrum-editing.png");
            target.IsEditing = false;
            source.Silent = false;

            // Exercise the production WASAPI backend in this isolated package, with a short local tone.
            using var native = new WindowsAudioSpectrumSource();
            using var service = new AudioSpectrumService(native, NullLogger<AudioSpectrumService>.Instance, TimeProvider.System);
            using var lease = service.Acquire();
            using var otherSource = new WindowsAudioSpectrumSource();
            using var otherService = new AudioSpectrumService(otherSource, NullLogger<AudioSpectrumService>.Instance, TimeProvider.System);
            using var otherLease = otherService.Acquire();
            for (var attempt = 0; attempt < 200 && (!service.State.IsAvailable || !otherService.State.IsAvailable); ++attempt)
            {
                await Task.Delay(50);
            }

            Check(service.State.IsAvailable && otherService.State.IsAvailable,
                "Two UWP clients receive spectra from the same on-demand desktop host");
            var tone = await WriteSpectrumToneAsync();
            var player = new MediaElement { AutoPlay = false, Volume = 0.2 };
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var playbackError = "";
            player.MediaOpened += (_, _) => opened.TrySetResult();
            player.MediaFailed += (_, args) =>
            {
                playbackError = args.ErrorMessage;
                opened.TrySetException(new InvalidOperationException(args.ErrorMessage));
            };
            root.Children.Add(player);
            using var stream = await tone.OpenReadAsync();
            player.SetSource(stream, "audio/wav");
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            player.Play();
            var captured = false;
            var maximumPower = 0f;
            var maximumBin = 0;
            for (var attempt = 0; attempt < 80; ++attempt)
            {
                await Task.Delay(50);
                var state = service.State;
                if (state.IsAvailable && state.Power.Length > 1)
                {
                    var power = state.Power.ToArray();
                    if (power.Max() > maximumPower)
                    {
                        maximumPower = power.Max();
                        maximumBin = Array.IndexOf(power, maximumPower);
                    }
                    var bin = (int)Math.Round(1000d * (state.Power.Length - 1) * 2 / state.SampleRate);
                    if (bin > 0 && bin < state.Power.Length && state.Power.Span[bin] > 1e-8)
                    {
                        captured = true;
                        break;
                    }
                }
            }

            var playbackState = player.CurrentState;
            var playbackPosition = player.Position;
            player.Stop();
            root.Children.Remove(player);
            Check(captured, $"Production loopback captures the 1 kHz output tone (available={service.State.IsAvailable}, rate={service.State.SampleRate}, peak={maximumPower:E3}, bin={maximumBin}, player={playbackState}/{playbackPosition}, error={playbackError})");
            lease.Dispose();
            await service.StopAsync(default);
            Check(!service.State.IsAvailable, "Disconnecting a UWP client clears its local spectrum state");
            await Task.Delay(100);
            Check(otherService.State.IsAvailable && otherService.State.Timestamp > DateTimeOffset.UtcNow.AddSeconds(-1),
                "Disconnecting one client preserves capture for the other client");
            otherLease.Dispose();
            await otherService.StopAsync(default);
            var hostStopped = false;
            var hostLock = System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, AudioSpectrumProtocol.RequestDirectory, "host.lock");
            for (var attempt = 0; attempt < 100 && !hostStopped; ++attempt)
            {
                await Task.Delay(50);
                try
                {
                    using var unlocked = new FileStream(hostLock, FileMode.Open, FileAccess.Read, FileShare.None);
                    hostStopped = true;
                }
                catch (IOException)
                {
                }
            }

            Check(hostStopped, "The desktop host exits automatically after its final client disconnects");
            target.Settings.ColorSource = SpectrumColorSource.Foreground;
            target.Foreground = gradient;
        }
        catch (Exception exception)
        {
            _report.Add("FAIL: " + exception);
        }

        _status.Text = _report.Any(line => line.StartsWith("FAIL:")) ? "FAIL: 查看 spectrum-report.txt" : $"PASS: {_report.Count} spectrum checks";
        var report = await ApplicationData.Current.LocalFolder.CreateFileAsync("spectrum-report.txt", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteLinesAsync(report, _report);
    }

    private static async Task<StorageFile> WriteSpectrumToneAsync()
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.ASCII, true))
        {
            const int samples = 48000 * 4;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + samples * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(48000);
            writer.Write(96000);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(samples * 2);
            for (var index = 0; index < samples; ++index)
            {
                writer.Write((short)(6000 * Math.Sin(2 * Math.PI * 1000 * index / 48000)));
            }
        }

        var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("spectrum-tone.wav", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteBytesAsync(file, bytes.ToArray());
        return file;
    }

    private async Task VerifySpectrumPixelsAsync(AudioSpectrumComponent view)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(view);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var image = (pixels, bitmap.PixelWidth, bitmap.PixelHeight);
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var vertical = view.Settings.Dock is SpectrumDock.Left or SpectrumDock.Right;
        var count = vertical ? height : width;
        var basePainted = 0;
        var tipPainted = 0;
        for (var index = 0; index < count; ++index)
        {
            var (baseX, baseY, tipX, tipY) = view.Settings.Dock switch
            {
                SpectrumDock.Top => (index, 1, index, height - 2),
                SpectrumDock.Bottom => (index, height - 2, index, 1),
                SpectrumDock.Left => (1, index, width - 2, index),
                _ => (width - 2, index, 1, index)
            };
            basePainted += PixelAt(image, baseX, baseY).A > 0 ? 1 : 0;
            tipPainted += PixelAt(image, tipX, tipY).A > 0 ? 1 : 0;
        }
        Check(basePainted > 0 && tipPainted == 0,
            $"{view.Settings.Dock} pixels grow from the selected edge and leave the far edge transparent");
    }

    private static async Task<int> CountSpectrumPixelsAsync(AudioSpectrumComponent view)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(view);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        return Enumerable.Range(0, pixels.Length / 4).Count(index => pixels[index * 4 + 3] != 0);
    }

    private sealed class PreviewSpectrumService : IAudioSpectrumService
    {
        private readonly float[] _power = Enumerable.Range(0, 2049).Select(index => (float)(0.001 + 0.04 * Math.Pow(Math.Sin(index * 0.1), 2))).ToArray();
        private readonly float[] _silence = new float[2049];
        internal bool Silent;
        internal int Users;
        public AudioSpectrumSnapshot State => new() { IsAvailable = true, Power = Silent ? _silence : _power, SampleRate = 48000, Timestamp = DateTimeOffset.UtcNow };
        public event EventHandler? Changed { add { } remove { } }
        public IDisposable Acquire()
        {
            Interlocked.Increment(ref Users);
            return System.Reactive.Disposables.Disposable.Create(() => Interlocked.Decrement(ref Users));
        }
    }

    private sealed class SpectrumMediaState : IMediaStateService
    {
        private MediaState _current = MediaState.Empty;
        internal MediaState Current
        {
            get => _current;
            set
            {
                _current = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
            }
        }
        public MediaState State => _current;
        public MediaState Placeholder => MediaState.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;
        public Task<bool> ExecuteAsync(MediaAction action, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private async Task VerifySpectrumSecondViewAsync(PreviewSpectrumService source, SpectrumMediaState media, IComponentsService components)
    {
        var secondView = CoreApplication.CreateNewView();
        var completion = new TaskCompletionSource<(int Id, AudioSpectrumComponent View)>(TaskCreationOptions.RunContinuationsAsynchronously);
        await secondView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            try
            {
                var view = (AudioSpectrumComponent)components.CreateComponent(new ComponentSettings { Id = AudioSpectrumComponent.Id })!;
                Window.Current.Content = view;
                Window.Current.Activate();
                completion.TrySetResult((ApplicationView.GetForCurrentView().Id, view));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        var second = await completion.Task;
        try
        {
            await ApplicationViewSwitcher.TryShowAsStandaloneAsync(second.Id);
            await Task.Delay(100);
            Check(source.Users == 5, "A second UI thread acquires the same shared spectrum service");
            await Task.Run(() => media.Current = new MediaState { HasSession = true, Cover = new byte[] { 1 }, ThemeColor = 0xFF23A5E7 });
            await Task.Delay(100);
            var valid = false;
            await secondView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                valid = ((Rectangle)second.View.Content).Fill is SolidColorBrush { Color: var color }
                    && color == Color.FromArgb(255, 35, 165, 231);
            });
            Check(valid, "The second view builds its own theme brush on its own Dispatcher");
        }
        finally
        {
            await secondView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => Window.Current.Close());
        }
        await Task.Delay(100);
        Check(source.Users == 4, "Closing the second window preserves the other views' leases");
    }
}
