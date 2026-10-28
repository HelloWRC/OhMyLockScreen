using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas.UI.Xaml;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Controls.MediaControls;
using OhMyLockScreen.Shared.Models.Media;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async Task VerifyMediaThreadingAsync(StackPanel root, MediaState state)
    {
        // Start with the awaited resource task so a regression is reported by the preview's
        // normal test boundary before exercising any async-void interaction handlers.
        var cover = new CoverImageControl { State = state, Width = 160, Height = 120, ShowSideDim = false };
        root.Children.Add(cover);
        await Task.Delay(200);
        var canvas = Descendants(cover).OfType<CanvasControl>().Single();
        var bitmapField = typeof(CoverImageControl).GetField("_bitmap", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(CoverImageControl), "_bitmap");
        await WaitForMediaThreadingAsync(() => bitmapField.GetValue(cover) != null, "Initial cover image loading");
        var loadResources = GetMediaPrivateMethod(typeof(CoverImageControl), "LoadResourcesAsync");
        await StartMediaWorkWithoutContextAsync(() =>
            (Task)(loadResources.Invoke(cover, [canvas])
                ?? throw new InvalidOperationException("Cover resource loading did not return a task.")))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Check(true, "Native cover resources load without an ambient synchronization context");

        // A cache miss also exercises the asynchronous encoded-image path under the same
        // condition; this uses the supplied preview image rather than a live media session.
        var requestedField = typeof(CoverImageControl).GetField("_hasRequest", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(CoverImageControl), "_hasRequest");
        requestedField.SetValue(cover, false);
        var loadCover = GetMediaPrivateMethod(typeof(CoverImageControl), "LoadCoverAsync");
        await StartMediaWorkWithoutContextAsync(() =>
            (Task)(loadCover.Invoke(cover, null)
                ?? throw new InvalidOperationException("Cover image loading did not return a task.")))
            .WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForMediaThreadingAsync(() => bitmapField.GetValue(cover) != null, "Context-free cover image loading");
        await Task.Delay(100);
        var rendered = new RenderTargetBitmap();
        await rendered.RenderAsync(cover);
        var pixels = (await rendered.GetPixelsAsync()).ToArray();
        Check(HasMediaImageDetail(pixels), "Context-free cover loading renders the image instead of a solid fallback");
        root.Children.Remove(cover);

        var playable = state with
        {
            HasSession = true,
            IsPlaying = false,
            CanPlayPause = true,
            CanSeek = true,
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(239),
            MinSeekTime = TimeSpan.Zero,
            MaxSeekTime = TimeSpan.FromSeconds(239),
            Position = TimeSpan.FromSeconds(14)
        };
        var commandService = new ThreadingMediaService(playable);
        var controller = new MediaController { Service = commandService, State = playable, Width = 80, Height = 40 };
        root.Children.Add(controller);
        await Task.Delay(50);
        var button = Descendants(controller).OfType<Button>().Single();
        var click = GetMediaPrivateMethod(typeof(MediaController), "OnClick");
        await StartMediaWorkWithoutContextAsync(() =>
        {
            click.Invoke(controller, [button, new RoutedEventArgs()]);
            return commandService.Started;
        });
        Check(!button.IsEnabled, "An asynchronous native media command disables its button while pending");
        await commandService.CompleteOnWorkerAsync();
        await WaitForMediaThreadingAsync(() => button.IsEnabled, "Context-free playback command completion");
        Check(commandService.CommandCount == 1, "A context-free playback command restores its native button on completion");

        // Keep the same native control while its visual lifetime changes during a command.
        var reloadService = new ThreadingMediaService(playable);
        controller.Service = reloadService;
        await StartMediaWorkWithoutContextAsync(() =>
        {
            click.Invoke(controller, [button, new RoutedEventArgs()]);
            return reloadService.Started;
        });
        root.Children.Remove(controller);
        await Task.Delay(50);
        root.Children.Add(controller);
        await Task.Delay(50);
        await reloadService.CompleteOnWorkerAsync();
        await WaitForMediaThreadingAsync(() => button.IsEnabled, "Reloaded playback command completion");
        Check(reloadService.CommandCount == 1, "A pending media command completes safely after native control unload and reload");
        root.Children.Remove(controller);

        var seekService = new ThreadingMediaService(playable);
        var progress = new MediaProgressBar { Service = seekService, State = playable, Width = 240, Height = 40 };
        root.Children.Add(progress);
        await Task.Delay(50);
        var slider = Descendants(progress).OfType<Slider>().Single();
        var commit = GetMediaPrivateMethod(typeof(MediaProgressBar), "CommitPosition");
        await StartMediaWorkWithoutContextAsync(() =>
        {
            commit.Invoke(progress, [40.0]);
            return seekService.Started;
        });
        progress.State = playable with { Position = TimeSpan.FromSeconds(23) };
        Check(Math.Abs(slider.Value - 14) < 0.01, "A pending native seek retains its displayed position until completion");
        await seekService.CompleteOnWorkerAsync();
        await WaitForMediaThreadingAsync(() => Math.Abs(slider.Value - 23) < 0.01, "Context-free seek completion");
        Check(seekService.LastSeek == TimeSpan.FromSeconds(40) && seekService.CommandCount == 1,
            "A context-free seek restores the native slider from the latest media snapshot");
        root.Children.Remove(progress);
    }

    private static async Task StartMediaWorkWithoutContextAsync(Func<Task> start)
    {
        Task task;
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            task = start();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        await task;
    }

    private static MethodInfo GetMediaPrivateMethod(Type type, string name)
        => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, name);

    private static async Task WaitForMediaThreadingAsync(Func<bool> condition, string operation)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(5))
            {
                throw new TimeoutException(operation + " did not complete on its UI view.");
            }

            await Task.Delay(10);
        }
    }

    private static bool HasMediaImageDetail(byte[] pixels)
    {
        int? first = null;
        for (var offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] < 128)
            {
                continue;
            }

            var color = pixels[offset] | pixels[offset + 1] << 8 | pixels[offset + 2] << 16;
            first ??= color;
            if (color != first.Value)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ThreadingMediaService(MediaState state) : IMediaStateService
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MediaState State => state;
        public MediaState Placeholder => state;
        public Task Started => _started.Task;
        public int CommandCount { get; private set; }
        public TimeSpan LastSeek { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }

        public Task<bool> ExecuteAsync(MediaAction action, CancellationToken cancellationToken = default) => WaitAsync();

        public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
        {
            LastSeek = position;
            return WaitAsync();
        }

        private async Task<bool> WaitAsync()
        {
            CommandCount++;
            _started.TrySetResult();
            return await _completion.Task.ConfigureAwait(false);
        }

        public async Task CompleteOnWorkerAsync()
        {
            await Task.Run(() => _completion.TrySetResult(true));
        }
    }
}
