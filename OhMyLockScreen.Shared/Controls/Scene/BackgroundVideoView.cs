using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using SharpDX;
using SharpDX.DXGI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.Scene;

/// <summary>A video surface whose native resources belong to its presenter's lifetime.</summary>
internal sealed class BackgroundVideoView : Grid, IVideoView, IDisposable
{
    private readonly SwapChainPanel _panel = new();
    private VideoSwapChain? _swapChain;
    private bool _loaded;
    private bool _disposed;

    internal event EventHandler<InitializedEventArgs>? Initialized;
    internal event EventHandler? Releasing;
    internal event EventHandler<string>? Failed;

    public MediaPlayer? MediaPlayer { get; set; }

    internal BackgroundVideoView()
    {
        Children.Add(_panel);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _panel.SizeChanged += OnSizeChanged;
        _panel.CompositionScaleChanged += OnCompositionScaleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        UpdateSwapChain();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        ReleaseSwapChain();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => UpdateSwapChain();

    private void OnCompositionScaleChanged(SwapChainPanel sender, object args) => UpdateSwapChain();

    private void UpdateSwapChain()
    {
        if (_disposed || !_loaded || _panel.ActualWidth <= 0 || _panel.ActualHeight <= 0)
        {
            return;
        }

        try
        {
            var width = Math.Max(1, (int)Math.Round(_panel.ActualWidth * _panel.CompositionScaleX));
            var height = Math.Max(1, (int)Math.Round(_panel.ActualHeight * _panel.CompositionScaleY));
            if (_swapChain != null)
            {
                _swapChain.UpdateSize(width, height, _panel.CompositionScaleX, _panel.CompositionScaleY);
                return;
            }

            _swapChain = new VideoSwapChain(width, height, _panel.CompositionScaleX, _panel.CompositionScaleY);
            using (var panel = ComObject.As<ISwapChainPanelNative>(_panel))
            {
                panel.SwapChain = _swapChain.SwapChain;
            }

            Initialized?.Invoke(this, new InitializedEventArgs(_swapChain.Options));
        }
        catch (Exception exception)
        {
            ReleaseSwapChain();
            Failed?.Invoke(this, $"无法初始化背景视频：{exception.Message}");
        }
    }

    internal void Trim() => _swapChain?.Trim();

    private void ReleaseSwapChain()
    {
        var chain = _swapChain;
        if (chain == null)
        {
            return;
        }

        _swapChain = null;
        try
        {
            // A child can unload before ScenePresenter; VLC must stop using these pointers first.
            Releasing?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            try
            {
                using var panel = ComObject.As<ISwapChainPanelNative>(_panel);
                panel.SwapChain = null;
            }
            finally
            {
                chain.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _panel.SizeChanged -= OnSizeChanged;
        _panel.CompositionScaleChanged -= OnCompositionScaleChanged;
        ReleaseSwapChain();
        MediaPlayer = null;
    }
}
