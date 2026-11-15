using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.System.UserProfile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

internal static class SystemBackgroundTests
{
    internal static async Task RunAsync(Action<string, Action> run, Func<string, Func<Task>, Task> runAsync)
    {
        run("Static wallpaper and video retain separate blob IDs across scene serialization", () =>
        {
            using var legacy = new MemoryStream("{\"Background\":{\"Mode\":1,\"BlobId\":\"video\"}}"u8.ToArray());
            var scene = ConfigureFileHelper.LoadConfigUnWrapped<SceneSettings>(legacy);
            Check(scene.Background is { Mode: BackgroundMode.Video, BlobId: "video", ImageBlobId: null },
                "Adding static wallpaper changed how existing video settings are loaded.");

            scene.Background.Mode = BackgroundMode.Image;
            scene.Background.ImageBlobId = "image";
            using var saved = new MemoryStream();
            ConfigureFileHelper.SaveConfig(saved, scene);
            saved.Position = 0;
            var restored = ConfigureFileHelper.LoadConfigUnWrapped<SceneSettings>(saved);
            Check(restored.Background is { Mode: BackgroundMode.Image, BlobId: "video", ImageBlobId: "image" },
                "Saving static wallpaper discarded the video or image asset reference.");
        });

        run("The editor underlay refreshes once on window reactivation and is released on unload", () =>
        {
            var window = Window.Current;
            var streams = new List<ImageStream>();
            LockScreen.OpenImage = () =>
            {
                var stream = new ImageStream();
                streams.Add(stream);
                return stream;
            };
            var presenter = CreatePresenter();
            var lockView = new ScenePresenter { Scene = new SceneSettings() };
            lockView.Load();
            Check(streams.Count == 0 && lockView.SystemBackgroundElement.Source == null && lockView.WallpaperElement.Source == null,
                "None mode loaded or showed wallpaper on the lock view.");
            lockView.Unload();
            var subscribers = window.ActivationSubscribers;
            presenter.Load();
            Check(streams.Count == 1 && streams[0].Disposed &&
                presenter.SystemBackgroundElement.Source is BitmapImage { SourceStream: var source } && ReferenceEquals(source, streams[0]),
                "Loading the editor did not decode the image and release its stream.");
            window.Activate();
            window.Activate();
            Check(streams.Count == 1, "Repeated active notifications reloaded the image.");
            window.Deactivate();
            window.Activate();
            Check(streams.Count == 2 && streams[1].Disposed, "Returning to the editor did not refresh the image.");
            presenter.Unload();
            window.Deactivate();
            window.Activate();
            Check(presenter.SystemBackgroundElement.Source == null && presenter.WallpaperElement.Source == null && streams.Count == 2 &&
                window.ActivationSubscribers == subscribers, "Unloading retained an image or window event subscription.");
            presenter.Load();
            Check(streams.Count == 3, "Reloading the editor did not read a fresh image.");
            presenter.Unload();
        });

        run("Static mode reads the system wallpaper and reuses the editor's decoded image when switching modes", () =>
        {
            var reads = 0;
            LockScreen.OpenImage = () => { reads++; return new ImageStream(); };
            var scene = new SceneSettings();
            var editor = CreatePresenter();
            editor.Scene = scene;
            var lockView = new ScenePresenter { Scene = scene };
            editor.Load();
            lockView.Load();
            var preview = editor.SystemBackgroundElement.Source;
            scene.Background.Mode = BackgroundMode.Image;
            Check(reads == 2 && preview != null && ReferenceEquals(editor.WallpaperElement.Source, preview) &&
                editor.SystemBackgroundElement.Source == null && lockView.WallpaperElement.Source is BitmapImage,
                "Static mode did not move the editor bitmap into the wallpaper layer or load one on the lock view.");

            Window.Current.Deactivate();
            Window.Current.Activate();
            Check(reads == 4 && lockView.WallpaperElement.Source is BitmapImage &&
                editor.WallpaperElement.Source is BitmapImage && !ReferenceEquals(editor.WallpaperElement.Source, preview),
                "Reactivation did not refresh the static wallpaper.");

            scene.Background.Mode = BackgroundMode.None;
            Check(editor.SystemBackgroundElement.Source != null && editor.WallpaperElement.Source == null &&
                lockView.WallpaperElement.Source == null && Window.Current.ActivationSubscribers == 1,
                "Switching out of static mode retained the foreground image or lock view subscription.");
            lockView.Unload();
            editor.Unload();
        });

        await runAsync("A custom static image supersedes pending system wallpaper and resolves through the scene blob context", async () =>
        {
            var presenter = CreatePresenter(preview: false);
            var scene = new SceneSettings();
            scene.Background.Mode = BackgroundMode.Image;
            presenter.Scene = scene;
            LockScreen.OpenImage = () => new ImageStream();
            presenter.Load();
            var completion = new TaskCompletionSource();
            var old = new ImageStream(completion.Task);
            LockScreen.OpenImage = () => old;
            var pending = presenter.RefreshSystemBackgroundForTestAsync();

            scene.Background.ImageBlobId = "custom";
            var custom = presenter.WallpaperElement.Source as BitmapImage;
            var expected = new Uri($"ms-appdata:///local/Scenes/{scene.SceneId:N}/custom");
            Check(custom?.UriSource == expected, "The image was not resolved for the current scene.");
            completion.SetResult();
            await pending;
            Check(old.Disposed && ReferenceEquals(custom, presenter.WallpaperElement.Source),
                "A pending system image displaced the custom wallpaper or retained its stream.");

            var failures = 0;
            presenter.BackgroundFailed += (_, _) => failures++;
            custom!.FailImage();
            Check(failures == 1 && presenter.WallpaperElement.Source == null, "A failed custom image remained in the wallpaper layer.");
            LockScreen.OpenImage = () => new ImageStream();
            scene.Background.ImageBlobId = null;
            Check(presenter.WallpaperElement.Source is BitmapImage { SourceStream: not null }, "Clearing the asset did not restore system wallpaper.");
            presenter.Unload();
        });

        await runAsync("System image read and decode failures clear the image and release streams", async () =>
        {
            var presenter = CreatePresenter();
            LockScreen.OpenImage = () => new ImageStream();
            presenter.Load();
            LockScreen.OpenImage = () => throw new UnauthorizedAccessException("Simulated denied capability.");
            await presenter.RefreshSystemBackgroundForTestAsync();
            Check(presenter.SystemBackgroundElement.Source == null, "A read failure retained the previous image.");
            var invalid = new ImageStream(Task.FromException(new InvalidDataException("Simulated corrupt image.")));
            LockScreen.OpenImage = () => invalid;
            await presenter.RefreshSystemBackgroundForTestAsync();
            Check(invalid.Disposed && presenter.SystemBackgroundElement.Source == null, "A decode failure leaked its stream or image.");
            LockScreen.OpenImage = () => new ImageStream();
            await presenter.RefreshSystemBackgroundForTestAsync();
            Check(presenter.SystemBackgroundElement.Source != null, "A later refresh could not recover after failure.");
            presenter.Unload();
        });

        await runAsync("An older system image refresh cannot replace a newer result", async () =>
        {
            var presenter = CreateLoadedPresenter();
            var completion = new TaskCompletionSource();
            var old = new ImageStream(completion.Task);
            LockScreen.OpenImage = () => old;
            var pending = presenter.RefreshSystemBackgroundForTestAsync();
            Check(!old.Disposed, "The image stream was closed before decoding finished.");
            LockScreen.OpenImage = () => new ImageStream();
            await presenter.RefreshSystemBackgroundForTestAsync();
            var current = presenter.SystemBackgroundElement.Source;
            completion.SetResult();
            await pending;
            Check(old.Disposed && ReferenceEquals(current, presenter.SystemBackgroundElement.Source),
                "An old decode replaced the current image or retained its stream.");
            presenter.Unload();
        });

        await runAsync("An older system image failure cannot clear a newer result", async () =>
        {
            var presenter = CreateLoadedPresenter();
            var completion = new TaskCompletionSource();
            var old = new ImageStream(completion.Task);
            LockScreen.OpenImage = () => old;
            var pending = presenter.RefreshSystemBackgroundForTestAsync();
            LockScreen.OpenImage = () => new ImageStream();
            await presenter.RefreshSystemBackgroundForTestAsync();
            var current = presenter.SystemBackgroundElement.Source;
            completion.SetException(new InvalidDataException("Late decode failure."));
            await pending;
            Check(old.Disposed && current != null && ReferenceEquals(current, presenter.SystemBackgroundElement.Source),
                "An old failure cleared a newer image.");
            presenter.Unload();
        });

        await runAsync("Unloading prevents a pending image decode from repopulating the presenter", async () =>
        {
            var presenter = CreateLoadedPresenter();
            var completion = new TaskCompletionSource();
            var stream = new ImageStream(completion.Task);
            LockScreen.OpenImage = () => stream;
            var pending = presenter.RefreshSystemBackgroundForTestAsync();
            presenter.Unload();
            completion.SetResult();
            await pending;
            Check(stream.Disposed && presenter.SystemBackgroundElement.Source == null,
                "An unloaded editor retained the decoded image or its stream.");
        });

        await runAsync("Unloading invalidates image requests even when the editor reloads before completion", async () =>
        {
            var presenter = CreateLoadedPresenter();
            var completion = new TaskCompletionSource();
            var old = new ImageStream(completion.Task);
            LockScreen.OpenImage = () => old;
            var pending = presenter.RefreshSystemBackgroundForTestAsync();
            presenter.Unload();
            LockScreen.OpenImage = () => new ImageStream();
            presenter.Load();
            var current = presenter.SystemBackgroundElement.Source;
            completion.SetResult();
            await pending;
            Check(old.Disposed && ReferenceEquals(current, presenter.SystemBackgroundElement.Source),
                "A request from a previous page lifetime repopulated its image.");
            presenter.Unload();
        });

        run("The system image uses the fixed background viewport while editor panels animate", () =>
        {
            var presenter = new ScenePresenter { BackgroundViewportSize = new Size(1920, 1080) };
            var image = presenter.WallpaperElement;
            foreach (var width in new[] { 1920d, 1763.25, 1401.75, 1238, 1920 })
            {
                presenter.BackgroundViewportElement.Resize(width, 1080);
                Check(image.Width == 1920 && image.Height == 1080 && image.HorizontalAlignment == HorizontalAlignment.Center &&
                    image.VerticalAlignment == VerticalAlignment.Center &&
                    presenter.BackgroundViewportElement.Clip!.Rect == new Rect(0, 0, width, 1080),
                    "Panel animation resized or failed to clip the image surface.");
            }

            presenter.BackgroundViewportSize = default;
            Check(double.IsNaN(image.Width) && double.IsNaN(image.Height) && image.HorizontalAlignment == HorizontalAlignment.Stretch &&
                image.VerticalAlignment == VerticalAlignment.Stretch, "Clearing fixed dimensions did not restore image layout.");
        });
    }

    private static ScenePresenter CreatePresenter(bool preview = true)
    {
        IAppHost.Host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton<FeatureRegistryService>();
            services.AddSingleton<IFeatureService, FeatureService>();
            services.AddSingleton<ISceneBlobStorageService, TestBlobStorage>();
        }).Build();
        return new ScenePresenter { ShowSystemBackgroundPreview = preview };
    }

    private sealed class TestBlobStorage : ISceneBlobStorageService
    {
        public Uri? GetBlobUri(Guid sceneId, string? blobId) => blobId is { } id
            ? new Uri($"ms-appdata:///local/Scenes/{sceneId:N}/{id}")
            : null;

        public Task<string> ImportAsync(Guid sceneId, Stream source, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static ScenePresenter CreateLoadedPresenter()
    {
        LockScreen.OpenImage = () => new ImageStream();
        var presenter = CreatePresenter();
        presenter.Load();
        return presenter;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ImageStream(Task? decode = null) : IRandomAccessStream
    {
        internal bool Disposed { get; private set; }

        public Task DecodeAsync() => decode ?? Task.CompletedTask;

        public void Dispose() => Disposed = true;
    }
}
