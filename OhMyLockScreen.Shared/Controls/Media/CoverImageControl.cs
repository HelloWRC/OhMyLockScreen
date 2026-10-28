using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Svg;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Models.Media;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Controls.MediaControls;

/// <summary>封面按控件大小居中裁剪，原始 SVG 遮罩控制边缘的主题色模糊。</summary>
public sealed class CoverImageControl : MediaPartControl
{
    public static readonly DependencyProperty ShowSideDimProperty = DependencyProperty.Register(
        nameof(ShowSideDim), typeof(bool), typeof(CoverImageControl),
        new PropertyMetadata(true, (sender, _) => ((CoverImageControl)sender)._canvas?.Invalidate()));

    public static readonly DependencyProperty IsPlaceholderProperty = DependencyProperty.Register(
        nameof(IsPlaceholder), typeof(bool), typeof(CoverImageControl),
        new PropertyMetadata(false, (sender, _) => ((CoverImageControl)sender).UpdateState()));

    private CanvasControl? _canvas;
    private CanvasDevice? _device;
    private CanvasBitmap? _bitmap;
    private CanvasRenderTarget? _edgeMask;
    private CanvasRenderTarget? _sideMask;
    private ReadOnlyMemory<byte> _requestedCover;
    private bool _requestedPlaceholder;
    private bool _hasRequest;
    private int _version;
    private int _resourceVersion;

    public bool ShowSideDim
    {
        get => (bool)GetValue(ShowSideDimProperty);
        set => SetValue(ShowSideDimProperty, value);
    }

    public bool IsPlaceholder
    {
        get => (bool)GetValue(IsPlaceholderProperty);
        set => SetValue(IsPlaceholderProperty, value);
    }

    public CoverImageControl()
    {
        Loaded += (_, _) => CreateCanvas();
        Unloaded += (_, _) => ReleaseCanvas();
    }

    private void CreateCanvas()
    {
        if (_canvas != null)
        {
            return;
        }

        _canvas = new CanvasControl();
        _canvas.CreateResources += OnCreateResources;
        _canvas.Draw += OnDraw;
        Content = _canvas;
    }

    private void OnCreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
        _device = sender.Device;
        ++_version;
        _bitmap?.Dispose();
        _bitmap = null;
        _hasRequest = false;
        args.TrackAsyncAction(LoadResourcesAsync(sender).AsAsyncAction());
    }

    private async Task LoadResourcesAsync(CanvasControl canvas)
    {
        // Capture the agile device on the UI thread; do not use CanvasControl after await.
        var device = canvas.Device;
        var version = ++_resourceVersion;
        CanvasRenderTarget? edge = null;
        CanvasRenderTarget? side = null;
        try
        {
            edge = await LoadMaskAsync(device, "CoverEdgeMask.svg", 509, 509).ConfigureAwait(false);
            side = await LoadMaskAsync(device, "CoverSideMask.svg", 509, 199).ConfigureAwait(false);
            Task? coverLoading = null;
            await RunOnViewAsync(() =>
            {
                if (version != _resourceVersion || !ReferenceEquals(canvas, _canvas))
                {
                    return;
                }

                _edgeMask?.Dispose();
                _sideMask?.Dispose();
                _edgeMask = edge;
                _sideMask = side;
                edge = side = null;
                coverLoading = LoadCoverAsync();
            }).ConfigureAwait(false);

            if (coverLoading != null)
            {
                await coverLoading.ConfigureAwait(false);
            }
        }
        finally
        {
            edge?.Dispose();
            side?.Dispose();
        }
    }

    private static async Task<CanvasRenderTarget> LoadMaskAsync(CanvasDevice device, string name, float width, float height)
    {
        var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///OhMyLockScreen.Shared/Assets/Media/{name}"));
        using var stream = await file.OpenReadAsync();
        using var document = await CanvasSvgDocument.LoadAsync(device, stream);
        var target = new CanvasRenderTarget(device, width, height, 96);
        using var session = target.CreateDrawingSession();
        session.Clear(Colors.Transparent);
        session.DrawSvg(document, new Size(width, height));
        return target;
    }

    protected override void UpdateState()
    {
        InvalidateMeasure();
        if (_canvas != null)
        {
            _ = LoadCoverAsync();
            _canvas.Invalidate();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Preserve Image-like intrinsic measurement for an unstyled independent cover.
        // Fixed layouts still control both dimensions and crop the image to those bounds.
        var width = availableSize.Width;
        var height = availableSize.Height;
        if (double.IsInfinity(width) && double.IsInfinity(height))
        {
            width = State.CoverWidth;
            height = State.CoverHeight;
        }
        else if (double.IsInfinity(height))
        {
            height = State.CoverWidth > 0 ? width * State.CoverHeight / State.CoverWidth : 0;
        }
        else if (double.IsInfinity(width))
        {
            width = State.CoverHeight > 0 ? height * State.CoverWidth / State.CoverHeight : 0;
        }

        var size = new Size(width, height);
        base.MeasureOverride(size);
        return size;
    }

    private async Task LoadCoverAsync()
    {
        var canvas = _canvas;
        var device = _device;
        var cover = State.Cover;
        var isPlaceholder = IsPlaceholder;
        if (canvas == null || device == null
            || (_hasRequest && _requestedCover.Equals(cover) && _requestedPlaceholder == isPlaceholder))
        {
            return;
        }

        var version = ++_version;
        _hasRequest = true;
        _requestedCover = cover;
        _requestedPlaceholder = isPlaceholder;
        _bitmap?.Dispose();
        _bitmap = null;
        CanvasBitmap? bitmap = null;
        try
        {
            if (!cover.IsEmpty)
            {
                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(cover.ToArray());
                    await writer.StoreAsync();
                }

                stream.Seek(0);
                bitmap = await CanvasBitmap.LoadAsync(device, stream);
            }

            await RunOnViewAsync(() =>
            {
                if (version == _version && ReferenceEquals(canvas, _canvas))
                {
                    _bitmap = bitmap;
                    bitmap = null;
                    canvas.Invalidate();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            IAppHost.TryGetService<ILogger<CoverImageControl>>()?.LogDebug(exception, "无法呈现媒体封面");
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        if (width <= 0 || height <= 0 || (!State.HasSession && !IsPlaceholder))
        {
            return;
        }

        var theme = State.ThemeColor;
        var dark = Color.FromArgb(255, (byte)((theme >> 16 & 255) * 0.58),
            (byte)((theme >> 8 & 255) * 0.58), (byte)((theme & 255) * 0.58));
        args.DrawingSession.Clear(dark);
        if (_bitmap == null)
        {
            return;
        }

        var scale = Math.Max(width / (float)_bitmap.Size.Width, height / (float)_bitmap.Size.Height);
        var imageWidth = (float)_bitmap.Size.Width * scale;
        var imageHeight = (float)_bitmap.Size.Height * scale;
        using var image = new Transform2DEffect
        {
            Source = _bitmap,
            TransformMatrix = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation((width - imageWidth) / 2, (height - imageHeight) / 2)
        };
        args.DrawingSession.DrawImage(image, new Rect(0, 0, width, height), new Rect(0, 0, width, height));

        using var blur = new GaussianBlurEffect { Source = image, BlurAmount = 8, BorderMode = EffectBorderMode.Hard };
        using var tint = new ColorSourceEffect { Color = Color.FromArgb(180, dark.R, dark.G, dark.B) };
        using var themedBlur = new CompositeEffect { Sources = { blur, tint } };
        if (_edgeMask != null)
        {
            // The edge mask belongs to the centered image square in the design.
            var size = Math.Max(width, height);
            DrawMasked(args.DrawingSession, themedBlur, _edgeMask,
                Matrix3x2.CreateScale(size / 509) * Matrix3x2.CreateTranslation((width - size) / 2, (height - size) / 2), width, height);
        }

        if (ShowSideDim && _sideMask != null)
        {
            DrawMasked(args.DrawingSession, themedBlur, _sideMask,
                Matrix3x2.CreateScale(width / 509, height / 165), width, height);
        }
    }

    private static void DrawMasked(CanvasDrawingSession session, ICanvasImage image, CanvasRenderTarget mask,
        Matrix3x2 transform, float width, float height)
    {
        using var scaledMask = new Transform2DEffect { Source = mask, TransformMatrix = transform };
        using var effect = new AlphaMaskEffect { Source = image, AlphaMask = scaledMask };
        session.DrawImage(effect, new Rect(0, 0, width, height), new Rect(0, 0, width, height));
    }

    private void ReleaseCanvas()
    {
        // CanvasControl owns the device; only release our reference to it.
        _device = null;
        ++_version;
        ++_resourceVersion;
        _hasRequest = false;
        _bitmap?.Dispose();
        _bitmap = null;
        _edgeMask?.Dispose();
        _edgeMask = null;
        _sideMask?.Dispose();
        _sideMask = null;
        if (_canvas is { } canvas)
        {
            _canvas = null;
            canvas.CreateResources -= OnCreateResources;
            canvas.Draw -= OnDraw;
            canvas.RemoveFromVisualTree();
            Content = null;
        }
    }
}
