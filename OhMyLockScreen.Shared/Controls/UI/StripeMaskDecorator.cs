using System.Numerics;
using System.Reactive.Disposables;
using Microsoft.Graphics.Canvas.Effects;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>对内容应用半透明斜条纹 Alpha 遮罩的可复用装饰器，并向外部保留未被隐藏的原始轮廓源。</summary>
public sealed class StripeMaskDecorator : ContentControl, IVisualRedirectSourceProvider
{
    public static readonly DependencyProperty IsMaskActiveProperty = DependencyProperty.Register(
        nameof(IsMaskActive),
        typeof(bool),
        typeof(StripeMaskDecorator),
        new PropertyMetadata(false, (sender, _) => ((StripeMaskDecorator)sender).SyncMask()));

    private FrameworkElement? _host;
    private FrameworkElement? _original;
    private FrameworkElement? _contentPresenter;
    private FrameworkElement? _activeHost;
    private Visual? _activeOriginalVisual;
    private CompositeDisposable? _resources;
    private bool _isLoaded;

    public bool IsMaskActive
    {
        get => GetValue(IsMaskActiveProperty) is true;
        set => SetValue(IsMaskActiveProperty, value);
    }

    public FrameworkElement? VisualRedirectSource => _contentPresenter;

    public event EventHandler? VisualRedirectSourceChanged;

    public StripeMaskDecorator()
    {
        DefaultStyleKey = typeof(StripeMaskDecorator);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnApplyTemplate()
    {
        ReleaseMask();
        base.OnApplyTemplate();
        _host = GetTemplateChild("PART_Host") as FrameworkElement;
        _original = GetTemplateChild("PART_Original") as FrameworkElement;
        _contentPresenter = GetTemplateChild("PART_Content") as FrameworkElement;
        VisualRedirectSourceChanged?.Invoke(this, EventArgs.Empty);
        SyncMask();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = true;
        SyncMask();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded)
        {
            _isLoaded = true;
            SyncMask();
            return;
        }

        _isLoaded = false;
        ReleaseMask();
    }

    private void SyncMask()
    {
        if (!IsMaskActive
            || !_isLoaded
            || _host is not { } host
            || _original is not { } original
            || _contentPresenter is not { } source)
        {
            ReleaseMask();
            return;
        }

        if (ReferenceEquals(_activeHost, host) && _resources != null)
        {
            return;
        }

        ReleaseMask();
        CreateMaskVisuals(host, original, source);
    }

    private void CreateMaskVisuals(FrameworkElement host, FrameworkElement original, FrameworkElement source)
    {
        var sourceVisual = ElementCompositionPreview.GetElementVisual(source);
        var compositor = sourceVisual.Compositor;
        var resources = new CompositeDisposable();
        try
        {
            var stripes = compositor.CreateLinearGradientBrush();
            resources.Add(stripes);
            stripes.MappingMode = CompositionMappingMode.Absolute;
            stripes.ExtendMode = CompositionGradientExtendMode.Wrap;
            stripes.EndPoint = new Vector2(10 / MathF.Sqrt(2));
            AddStop(0, 0xAF);
            AddStop(0.5f, 0xAF);
            AddStop(0.5f, 0x50);
            AddStop(1, 0x50);

            using var mask = new AlphaMaskEffect
            {
                Source = new CompositionEffectSourceParameter("Content"),
                AlphaMask = new CompositionEffectSourceParameter("Stripes")
            };
            var factory = compositor.CreateEffectFactory(mask);
            resources.Add(factory);
            var brush = factory.CreateBrush();
            resources.Add(brush);
            // 未绑定的 Content 参数在 LayerVisual 上接收子树（RedirectVisual）实时渲染输出。
            brush.SetSourceParameter("Stripes", stripes);

            var layer = compositor.CreateLayerVisual();
            resources.Add(layer);
            layer.RelativeSizeAdjustment = Vector2.One;
            layer.Effect = brush;

            var redirect = compositor.CreateRedirectVisual(sourceVisual);
            resources.Add(redirect);
            redirect.RelativeSizeAdjustment = Vector2.One;
            layer.Children.InsertAtTop(redirect);

            ElementCompositionPreview.SetElementChildVisual(host, layer);
            // XAML Opacity=0 会剔除源子树，因此在合成层隐藏 PART_Original 以保留 PART_Content 的正常输出。
            var originalVisual = ElementCompositionPreview.GetElementVisual(original);
            originalVisual.Opacity = 0;

            _activeHost = host;
            _activeOriginalVisual = originalVisual;
            _resources = resources;

            void AddStop(float offset, byte alpha)
            {
                var stop = compositor.CreateColorGradientStop(offset, Color.FromArgb(alpha, 255, 255, 255));
                resources.Add(stop);
                stripes.ColorStops.Add(stop);
            }
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }

    private void ReleaseMask()
    {
        var host = _activeHost;
        var originalVisual = _activeOriginalVisual;
        var resources = _resources;
        _activeHost = null;
        _activeOriginalVisual = null;
        _resources = null;

        if (originalVisual != null)
        {
            try
            {
                originalVisual.Opacity = 1;
            }
            catch
            {
            }
        }

        if (host != null)
        {
            try
            {
                ElementCompositionPreview.SetElementChildVisual(host, null);
            }
            catch
            {
            }
        }

        resources?.Dispose();
    }
}
