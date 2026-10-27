using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using OhMyLockScreen.Shared.Controls.Scene;
using SharpDX.Direct3D11;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace VideoBackgroundPreview;

public sealed partial class App : Application
{
    private readonly TextBlock _status = new() { Text = "Checking real UWP VLC frames…", Margin = new Thickness(16) };
    private StackPanel _root = null!;
    private readonly List<string> _report = [];
    private LibVLC? _vlc;
    private MediaPlayer? _player;
    private bool _started;

    public App()
    {
        UnhandledException += (_, args) =>
        {
            File.WriteAllText(Path.Combine(ApplicationData.Current.LocalFolder.Path, "report.txt"), "FAIL: " + args.Exception);
            args.Handled = true;
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // A separate package and app-data folder; no lock-screen extension, host or settings services.
        _root = new StackPanel { Background = new SolidColorBrush(Colors.Black) };
        _root.Children.Add(_status);
        _root.Loaded += VerifyPlayback;
        Window.Current.Content = _root;
        Window.Current.Closed += (_, _) => ReleasePlayer();
        Window.Current.Activate();
    }

    private async void VerifyPlayback(object sender, RoutedEventArgs args)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        try
        {
            await Task.Delay(500);
            var baseline = ReadGpuMemory();
            Report($"Baseline: {baseline}");
            for (var cycle = 0; cycle < 24; cycle++)
            {
                using var surface = new VideoSwapChain(640, 360, 1, 1);
                var options = surface.Options;
                var software = cycle is >= 4 and < 8;
                _vlc = new LibVLC(software ? [.. options, "--avcodec-hw=none"] : options);
                if (cycle == 0)
                {
                    Report($"VLC: {_vlc.Version}");
                }
                _player = new MediaPlayer(_vlc) { Mute = true };
                using var media = new Media(_vlc, Path.Combine(Package.Current.InstalledLocation.Path, "TestVideo.mp4"), FromType.FromPath);
                if (!_player.Play(media))
                {
                    throw new InvalidOperationException("The real UWP player could not start.");
                }

                var timer = Stopwatch.StartNew();
                if (cycle < 8)
                {
                    while (media.Statistics.DisplayedPictures < 5 && timer.Elapsed < TimeSpan.FromSeconds(15))
                    {
                        await Task.Delay(50);
                    }
                }
                else
                {
                    await Task.Delay(40);
                }

                var displayed = media.Statistics.DisplayedPictures;
                if (cycle == 0)
                {
                    // Exercise the Stop/Play loop used by ScenePresenter on the same rendering device.
                    for (var restart = 0; restart < 3; restart++)
                    {
                        _player.Stop();
                        if (!_player.Play())
                        {
                            throw new InvalidOperationException("The real UWP player could not restart.");
                        }

                        await Task.Delay(300);
                    }
                }

                var device = surface.SwapChain.GetDevice<Device>();
                var devicePointer = device.NativePointer;
                Marshal.AddRef(devicePointer);
                device.Dispose();
                ReleasePlayer();
                if (cycle == 0 && (displayed < 5 || !HasRenderedPixels(surface)))
                {
                    throw new InvalidOperationException($"Video output stayed black ({displayed} displayed frames).");
                }

                media.Dispose();
                surface.Dispose();
                await Task.Delay(100);
                var references = Marshal.Release(devicePointer);
                Report($"Cycle {cycle + 1}: software={software}, frames={displayed}, device refs={references}, {ReadGpuMemory()}");
                if (references != 0)
                {
                    throw new InvalidOperationException($"Cycle {cycle + 1} retained {references} native device references.");
                }
            }

            await Task.Delay(3000);
            var idle = ReadGpuMemory();
            Report($"Idle: {idle}");
            const long tolerance = 32 * 1024 * 1024;
            if (idle.Local > baseline.Local + tolerance || idle.Shared > baseline.Shared + tolerance)
            {
                throw new InvalidOperationException("GPU allocations did not return near the idle baseline.");
            }

            Report("PASS: Real UWP playback and rapid replacement checks completed.");
        }
        catch (Exception exception)
        {
            Report("FAIL: " + exception);
        }
    }

    private readonly record struct GpuMemory(long Local, long Shared)
    {
        public override string ToString() => $"local={Local / 1048576d:F1} MiB, shared={Shared / 1048576d:F1} MiB";
    }

    private static GpuMemory ReadGpuMemory()
    {
        long local = 0;
        long shared = 0;
        using var factory = new SharpDX.DXGI.Factory1();
        for (var index = 0; index < factory.GetAdapterCount1(); index++)
        {
            using var adapter = factory.GetAdapter1(index);
            using var memory = adapter.QueryInterface<SharpDX.DXGI.Adapter3>();
            local += memory.QueryVideoMemoryInfo(0, SharpDX.DXGI.MemorySegmentGroup.Local).CurrentUsage;
            shared += memory.QueryVideoMemoryInfo(0, SharpDX.DXGI.MemorySegmentGroup.NonLocal).CurrentUsage;
        }

        return new GpuMemory(local, shared);
    }

    private static bool HasRenderedPixels(VideoSwapChain surface)
    {
        var option = surface.Options[0];
        var pointer = (IntPtr)long.Parse(option[(option.IndexOf("0x", StringComparison.Ordinal) + 2)..],
            NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        Marshal.AddRef(pointer);
        using var context = new DeviceContext(pointer);
        using var source = surface.SwapChain.GetBackBuffer<Texture2D>(0);
        using var device = surface.SwapChain.GetDevice<Device>();
        var description = source.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CpuAccessFlags = CpuAccessFlags.Read;
        description.OptionFlags = ResourceOptionFlags.None;
        using var staging = new Texture2D(device, description);
        context.CopyResource(source, staging);
        var pixels = context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            for (var y = 0; y < description.Height; y += 16)
            {
                for (var x = 0; x < description.Width; x += 16)
                {
                    if ((Marshal.ReadInt32(pixels.DataPointer, y * pixels.RowPitch + x * 4) & 0x00ffffff) != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        finally
        {
            context.UnmapSubresource(staging, 0);
        }
    }

    private void ReleasePlayer()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        _vlc?.Dispose();
        _vlc = null;
    }

    private void Report(string message)
    {
        _report.Add(message);
        _status.Text = message;
        File.WriteAllLines(Path.Combine(ApplicationData.Current.LocalFolder.Path, "report.txt"), _report);
    }
}
