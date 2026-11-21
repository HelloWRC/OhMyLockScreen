using Microsoft.Graphics.Canvas;
using OhMyLockScreen.Shared.Controls.Scene;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    // RenderTargetBitmap can omit hand-in composition visuals. Capture only this test element.
    private static async Task<(byte[] Pixels, int Width, int Height)> CaptureComposedPixelsAsync(FrameworkElement element)
    {
        element.UpdateLayout();
        await Task.Delay(100);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var item = GraphicsCaptureItem.CreateFromVisual(visual);
        var size = new Windows.Graphics.SizeInt32((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight));
        var device = CanvasDevice.GetSharedDevice();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, size);
        using var session = pool.CreateCaptureSession(item);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.FrameArrived += (_, _) => ready.TrySetResult();
        session.StartCapture();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var frame = pool.TryGetNextFrame();
        using var bitmap = CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Surface);
        return (bitmap.GetPixelBytes(), (int)bitmap.SizeInPixels.Width, (int)bitmap.SizeInPixels.Height);
    }

    private async Task VerifyRuleStripesAsync(ComponentPresenter presenter, Action<bool> setRule)
    {
        var border = presenter.Border!;
        border.BorderThickness = new Thickness(4);
        border.BorderBrush = new SolidColorBrush(Colors.White);
        presenter.Height = 48;
        foreach (var width in new[] { 180, 300 })
        {
            presenter.Width = width;
            var image = await CaptureComposedPixelsAsync(presenter);
            var scale = image.Width / presenter.ActualWidth;
            byte Sample(double x, double y) => PixelAt(image, (int)(x * scale), (int)(y * scale)).A;
            var row = Enumerable.Range(0, 80).Select(x => Sample(x, 2)).ToArray();
            var rising = Enumerable.Range(1, row.Length - 1).Where(x => row[x - 1] < 128 && row[x] >= 128).ToArray();
            Check(row.Any(a => Math.Abs(a - 0x50) < 4) && row.Any(a => Math.Abs(a - 0xAF) < 4)
                && rising.Length >= 5 && rising.Zip(rising.Skip(1), (a, b) => b - a).All(d => d is >= 13 and <= 16),
                $"Hidden border (alpha {row.Min()}..{row.Max()}, edges {string.Join(",", rising)}) has alternating mask alpha and a stable 10 DIP diagonal period at width {width}");
            Check(Sample(width - 8, 38) == 0, "Stripes leave empty content transparent");
        }
        border.Background = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255));
        var tinted = await CaptureComposedPixelsAsync(presenter);
        var alphas = Enumerable.Range(10, 60).Select(x => PixelAt(tinted, x, tinted.Height - 10).A).ToArray();
        Check(alphas.Any(a => Math.Abs(a - 88) < 4) && alphas.Any(a => Math.Abs(a - 40) < 4),
            "Live background changes multiply the existing alpha");
        var pointer = border.TransformToVisual(null).TransformPoint(new Point(2, 2));
        var hits = VisualTreeHelper.FindElementsInHostCoordinates(pointer, presenter);
        Check(hits.Contains(border) && presenter.IsContentHitTestVisible, "Masked component retains its original pointer target");
        setRule(false);
        var visible = await CaptureComposedPixelsAsync(presenter);
        Check(PixelAt(visible, 20, 1).A == 255, "Rule clearing restores unmasked rendering while editing");
        var content = (FrameworkElement)presenter.Content;
        content.Visibility = Visibility.Collapsed;
        var selfHidden = await CaptureComposedPixelsAsync(presenter);
        Check(content.Visibility == Visibility.Visible && content.ActualHeight > 0 && PixelAt(selfHidden, 20, 1).A is > 0 and < 255,
            "A collapsed component keeps its content and gains stripes in the editor");
        content.ClearValue(UIElement.VisibilityProperty);
        var restored = await CaptureComposedPixelsAsync(presenter);
        Check(PixelAt(restored, 20, 1).A == 255, "Changing the underlying visibility releases the editor override");
        setRule(true);
        border.ClearValue(Border.BackgroundProperty);
        border.ClearValue(Border.BorderBrushProperty);
        border.ClearValue(Border.BorderThicknessProperty);
        presenter.ClearValue(FrameworkElement.WidthProperty);
        presenter.ClearValue(FrameworkElement.HeightProperty);
    }
}
