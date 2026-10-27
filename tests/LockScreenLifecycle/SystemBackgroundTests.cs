using OhMyLockScreen;
using OhMyLockScreen.Shared.Controls.Scene;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.System.UserProfile;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

internal static class SystemBackgroundTests
{
    internal static async Task RunAsync(Action<string, Action> run, Func<string, Func<Task>, Task> runAsync)
    {
        run("System image loads only for the editor and refreshes once on window reactivation", () =>
        {
            var window = Window.Current;
            var streams = new List<ImageStream>();
            LockScreen.OpenImage = () =>
            {
                var stream = new ImageStream();
                streams.Add(stream);
                return stream;
            };
            var page = new SceneEditorPage();
            Check(page.EditorPresenter.SystemBackgroundImage == null && new ScenePresenter().SystemBackgroundImage == null,
                "A presenter read or populated a system image without the editor.");
            var subscribers = window.ActivationSubscribers;
            page.LoadSystemBackground(window);
            Check(streams.Count == 1 && streams[0].Disposed &&
                ReferenceEquals(page.EditorPresenter.SystemBackgroundElement.Source, page.EditorPresenter.SystemBackgroundImage) &&
                page.EditorPresenter.SystemBackgroundImage is BitmapImage { SourceStream: var source } && ReferenceEquals(source, streams[0]),
                "Loading the editor did not decode the image and release its stream.");
            window.Activate();
            window.Activate();
            Check(streams.Count == 1, "Repeated active notifications reloaded the image.");
            window.Deactivate();
            window.Activate();
            Check(streams.Count == 2 && streams[1].Disposed, "Returning to the editor did not refresh the image.");
            page.UnloadSystemBackground();
            window.Deactivate();
            window.Activate();
            Check(page.EditorPresenter.SystemBackgroundImage == null && page.EditorPresenter.SystemBackgroundElement.Source == null && streams.Count == 2 &&
                window.ActivationSubscribers == subscribers, "Unloading retained an image or window event subscription.");
            page.LoadSystemBackground(window);
            Check(streams.Count == 3, "Reloading the editor did not read a fresh image.");
            page.UnloadSystemBackground();
        });

        await runAsync("System image read and decode failures clear the image and release streams", async () =>
        {
            var page = new SceneEditorPage();
            LockScreen.OpenImage = () => new ImageStream();
            page.LoadSystemBackground(Window.Current);
            LockScreen.OpenImage = () => throw new UnauthorizedAccessException("Simulated denied capability.");
            await page.RefreshSystemBackgroundForTestAsync();
            Check(page.EditorPresenter.SystemBackgroundImage == null, "A read failure retained the previous image.");
            var invalid = new ImageStream(Task.FromException(new InvalidDataException("Simulated corrupt image.")));
            LockScreen.OpenImage = () => invalid;
            await page.RefreshSystemBackgroundForTestAsync();
            Check(invalid.Disposed && page.EditorPresenter.SystemBackgroundImage == null, "A decode failure leaked its stream or image.");
            LockScreen.OpenImage = () => new ImageStream();
            await page.RefreshSystemBackgroundForTestAsync();
            Check(page.EditorPresenter.SystemBackgroundImage != null, "A later refresh could not recover after failure.");
            page.UnloadSystemBackground();
        });

        await runAsync("An older system image refresh cannot replace a newer result", async () =>
        {
            var page = CreateLoadedPage();
            var completion = new TaskCompletionSource();
            var old = new ImageStream(completion.Task);
            LockScreen.OpenImage = () => old;
            var pending = page.RefreshSystemBackgroundForTestAsync();
            Check(!old.Disposed, "The image stream was closed before decoding finished.");
            LockScreen.OpenImage = () => new ImageStream();
            await page.RefreshSystemBackgroundForTestAsync();
            var current = page.EditorPresenter.SystemBackgroundImage;
            completion.SetResult();
            await pending;
            Check(old.Disposed && ReferenceEquals(current, page.EditorPresenter.SystemBackgroundImage),
                "An old decode replaced the current image or retained its stream.");
            page.UnloadSystemBackground();
        });

        await runAsync("An older system image failure cannot clear a newer result", async () =>
        {
            var page = CreateLoadedPage();
            var completion = new TaskCompletionSource();
            var old = new ImageStream(completion.Task);
            LockScreen.OpenImage = () => old;
            var pending = page.RefreshSystemBackgroundForTestAsync();
            LockScreen.OpenImage = () => new ImageStream();
            await page.RefreshSystemBackgroundForTestAsync();
            var current = page.EditorPresenter.SystemBackgroundImage;
            completion.SetException(new InvalidDataException("Late decode failure."));
            await pending;
            Check(old.Disposed && current != null && ReferenceEquals(current, page.EditorPresenter.SystemBackgroundImage),
                "An old failure cleared a newer image.");
            page.UnloadSystemBackground();
        });

        await runAsync("Unloading prevents a pending image decode from repopulating the presenter", async () =>
        {
            var page = CreateLoadedPage();
            var completion = new TaskCompletionSource();
            var stream = new ImageStream(completion.Task);
            LockScreen.OpenImage = () => stream;
            var pending = page.RefreshSystemBackgroundForTestAsync();
            page.UnloadSystemBackground();
            completion.SetResult();
            await pending;
            Check(stream.Disposed && page.EditorPresenter.SystemBackgroundImage == null,
                "An unloaded editor retained the decoded image or its stream.");
        });

        await runAsync("Unloading invalidates image requests even when the editor reloads before completion", async () =>
        {
            var page = CreateLoadedPage();
            var completion = new TaskCompletionSource();
            var old = new ImageStream(completion.Task);
            LockScreen.OpenImage = () => old;
            var pending = page.RefreshSystemBackgroundForTestAsync();
            page.UnloadSystemBackground();
            LockScreen.OpenImage = () => new ImageStream();
            page.LoadSystemBackground(Window.Current);
            var current = page.EditorPresenter.SystemBackgroundImage;
            completion.SetResult();
            await pending;
            Check(old.Disposed && ReferenceEquals(current, page.EditorPresenter.SystemBackgroundImage),
                "A request from a previous page lifetime repopulated its image.");
            page.UnloadSystemBackground();
        });

        run("The system image uses the fixed background viewport while editor panels animate", () =>
        {
            var presenter = new ScenePresenter { BackgroundViewportSize = new Size(1920, 1080) };
            var image = presenter.SystemBackgroundElement;
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

    private static SceneEditorPage CreateLoadedPage()
    {
        LockScreen.OpenImage = () => new ImageStream();
        var page = new SceneEditorPage();
        page.LoadSystemBackground(Window.Current);
        return page;
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
