namespace Windows.Storage.Streams
{
    public interface IRandomAccessStream : IDisposable
    {
        Task DecodeAsync();
    }
}

namespace Windows.System.UserProfile
{
    public static class LockScreen
    {
        public static Func<Storage.Streams.IRandomAccessStream> OpenImage { get; set; } =
            () => throw new InvalidOperationException("No simulated lock screen image was configured.");

        public static Storage.Streams.IRandomAccessStream GetImageStream() => OpenImage();
    }
}

namespace Windows.UI.Xaml.Controls
{
    public sealed class Image : FrameworkElement
    {
        public Media.ImageSource? Source { get; set; }
    }
}

namespace OhMyLockScreen
{
    public sealed partial class SceneEditorPage
    {
        private Windows.UI.Xaml.Window? _window;
        private bool _loaded;
        internal Shared.Controls.Scene.ScenePresenter EditorPresenter { get; } = new();

        internal void LoadSystemBackground(Windows.UI.Xaml.Window window)
        {
            _loaded = true;
            _window = window;
            AttachSystemBackground();
        }

        internal void UnloadSystemBackground()
        {
            _loaded = false;
            DetachSystemBackground();
            _window = null;
        }

        internal Task RefreshSystemBackgroundForTestAsync() => RefreshSystemBackgroundAsync();
    }
}
