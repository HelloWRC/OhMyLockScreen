using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ScenePresenter
{
    public static readonly DependencyProperty BackgroundViewportSizeProperty = DependencyProperty.Register(
        nameof(BackgroundViewportSize), typeof(Size), typeof(ScenePresenter),
        new PropertyMetadata(default(Size), OnBackgroundViewportSizeChanged));

    private readonly RectangleGeometry _backgroundViewportClip = new();

    /// <summary>可选的固定背景渲染尺寸；默认随场景布局，编辑器使用完整视口尺寸避免侧栏动画反复调整视频交换链。</summary>
    public Size BackgroundViewportSize
    {
        get => (Size)GetValue(BackgroundViewportSizeProperty)!;
        set => SetValue(BackgroundViewportSizeProperty, value);
    }

    private void InitializeBackgroundLayout()
    {
        BackgroundViewport.Clip = _backgroundViewportClip;
        BackgroundViewport.SizeChanged += OnBackgroundViewportLayoutChanged;
    }

    private static void OnBackgroundViewportSizeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (ScenePresenter)sender;
        var size = (Size)args.NewValue!;
        var fixedSize = double.IsFinite(size.Width) && double.IsFinite(size.Height) && size.Width > 0 && size.Height > 0;
        presenter.BackgroundLayer.Width = fixedSize ? size.Width : double.NaN;
        presenter.BackgroundLayer.Height = fixedSize ? size.Height : double.NaN;
        presenter.BackgroundLayer.HorizontalAlignment = fixedSize ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        presenter.BackgroundLayer.VerticalAlignment = fixedSize ? VerticalAlignment.Center : VerticalAlignment.Stretch;
    }

    private void OnBackgroundViewportLayoutChanged(object sender, SizeChangedEventArgs args)
    {
        _backgroundViewportClip.Rect = new Rect(0, 0, BackgroundViewport.ActualWidth, BackgroundViewport.ActualHeight);
    }
}
