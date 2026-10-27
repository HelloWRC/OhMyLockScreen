using OhMyLockScreen.Shared.Controls.Scene;

// Exercise the real BackgroundVideoView lifecycle without creating a GPU device or a XAML window.
namespace OhMyLockScreen.Shared.Controls.Scene
{
    internal sealed class VideoSwapChain(int width, int height, double scaleX, double scaleY) : IDisposable
    {
        internal static List<VideoSwapChain> Created { get; } = [];
        internal static string[] NextOptions { get; set; } = [];
        internal static bool FailOnCreate { get; set; }
        internal bool IsDisposed { get; private set; }
        internal bool IsAttached { get; set; }
        internal Action? BeforeDispose { get; set; }
        internal int TrimCalls { get; private set; }
        internal (int Width, int Height, double ScaleX, double ScaleY) Size { get; private set; }
            = (width, height, scaleX, scaleY);
        internal VideoSwapChain SwapChain => this;
        internal string[] Options { get; } = CreateOptions();

        private static string[] CreateOptions()
        {
            if (FailOnCreate)
            {
                throw new InvalidOperationException("Simulated D3D creation failure.");
            }

            return NextOptions;
        }

        internal void UpdateSize(int width, int height, double scaleX, double scaleY)
            => Size = (width, height, scaleX, scaleY);
        internal void Trim() => TrimCalls++;

        public void Dispose()
        {
            BeforeDispose?.Invoke();
            if (IsAttached)
            {
                throw new InvalidOperationException("A swapchain was disposed while attached to XAML.");
            }

            IsDisposed = true;
        }
    }

    internal static class VideoSurfaceTestExtensions
    {
        internal static void Initialize(this BackgroundVideoView view, params string[] options)
        {
            VideoSwapChain.NextOptions = options;
            ((Windows.UI.Xaml.Controls.SwapChainPanel)view.Children.Single()).Resize(1920, 1080);
            view.Load();
        }

        internal static VideoSwapChain? GetSwapChain(this BackgroundVideoView view)
            => ((Windows.UI.Xaml.Controls.SwapChainPanel)view.Children.Single()).NativeSwapChain;
    }
}

namespace Windows.UI.Xaml.Controls
{
    public class SwapChainPanel : Panel
    {
        public double CompositionScaleX { get; set; } = 1;
        public double CompositionScaleY { get; set; } = 1;
        internal VideoSwapChain? NativeSwapChain { get; set; }
        public event Action<SwapChainPanel, object>? CompositionScaleChanged;
        public void ChangeScale() => CompositionScaleChanged?.Invoke(this, EventArgs.Empty);
    }
}

namespace SharpDX
{
    public static class ComObject
    {
        public static T As<T>(object panel) where T : DXGI.ISwapChainPanelNative, new()
            => new() { Panel = (Windows.UI.Xaml.Controls.SwapChainPanel)panel };
    }
}

namespace SharpDX.DXGI
{
    public class ISwapChainPanelNative : IDisposable
    {
        internal Windows.UI.Xaml.Controls.SwapChainPanel Panel { get; set; } = null!;
        internal VideoSwapChain? SwapChain
        {
            set
            {
                if (Panel.NativeSwapChain != null)
                {
                    Panel.NativeSwapChain.IsAttached = false;
                }

                Panel.NativeSwapChain = value;
                if (value != null)
                {
                    value.IsAttached = true;
                    VideoSwapChain.Created.Add(value);
                }
            }
        }

        public void Dispose() { }
    }
}
