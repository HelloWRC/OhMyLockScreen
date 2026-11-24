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
public sealed class StripeMaskDecorator : ContentControl, IVisualRedirectSourceProvider, IVisualOutsetProvider
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
    private CompositionPropertySet? _props;
    private CompositionLinearGradientBrush? _stripes;
    private LayerVisual? _layer;
    private RedirectVisual? _redirect;
    private IVisualOutsetProvider? _observedOutsetProvider;
    private double _lastOutset;
    private bool _isLoaded;

    public bool IsMaskActive
    {
        get => GetValue(IsMaskActiveProperty) is true;
        set => SetValue(IsMaskActiveProperty, value);
    }

    public FrameworkElement? VisualRedirectSource => _contentPresenter;

    public event EventHandler? VisualRedirectSourceChanged;

    public double VisualOutset => _observedOutsetProvider?.VisualOutset ?? 0;

    public event EventHandler? VisualOutsetChanged;

    public StripeMaskDecorator()
    {
        DefaultStyleKey = typeof(StripeMaskDecorator);
        RegisterPropertyChangedCallback(ContentProperty, (_, _) => UpdateObservedOutsetProvider());
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
        UpdateObservedOutsetProvider();
        SyncMask();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = true;
        UpdateObservedOutsetProvider();
        SyncMask();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded)
        {
            _isLoaded = true;
            UpdateObservedOutsetProvider();
            SyncMask();
            return;
        }

        _isLoaded = false;
        ReleaseObservedOutsetProvider();
        ReleaseMask();
    }

    private void UpdateObservedOutsetProvider()
    {
        var next = Content as IVisualOutsetProvider;
        if (!ReferenceEquals(_observedOutsetProvider, next))
        {
            if (_observedOutsetProvider != null)
            {
                _observedOutsetProvider.VisualOutsetChanged -= OnChildOutsetChanged;
            }

            _observedOutsetProvider = next;
            if (next != null)
            {
                next.VisualOutsetChanged += OnChildOutsetChanged;
            }
        }

        NotifyOutsetChanged();
    }

    private void ReleaseObservedOutsetProvider()
    {
        if (_observedOutsetProvider != null)
        {
            _observedOutsetProvider.VisualOutsetChanged -= OnChildOutsetChanged;
            _observedOutsetProvider = null;
        }

        NotifyOutsetChanged();
    }

    private void OnChildOutsetChanged(object? sender, EventArgs args) => NotifyOutsetChanged();

    private void NotifyOutsetChanged()
    {
        if (IsMaskActive && _isLoaded && _resources != null)
        {
            ApplyOutsetPadding();
        }

        var current = VisualOutset;
        if (_lastOutset != current)
        {
            _lastOutset = current;
            VisualOutsetChanged?.Invoke(this, EventArgs.Empty);
        }
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
            ApplyOutsetPadding();
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

            var props = compositor.CreatePropertySet();
            resources.Add(props);
            props.InsertScalar("pad", 0f);

            var layer = compositor.CreateLayerVisual();
            resources.Add(layer);
            layer.Effect = brush;

            var redirect = compositor.CreateRedirectVisual(sourceVisual);
            resources.Add(redirect);
            var redirectSizeAnim = compositor.CreateExpressionAnimation("source.Size");
            resources.Add(redirectSizeAnim);
            redirectSizeAnim.SetReferenceParameter("source", sourceVisual);
            redirect.StartAnimation(nameof(Visual.Size), redirectSizeAnim);
            layer.Children.InsertAtTop(redirect);

            var layerSizeAnim = compositor.CreateExpressionAnimation(
                "source.Size.X > 0 && source.Size.Y > 0 ? source.Size + Vector2(props.pad * 2, props.pad * 2) : Vector2(0, 0)");
            resources.Add(layerSizeAnim);
            layerSizeAnim.SetReferenceParameter("source", sourceVisual);
            layerSizeAnim.SetReferenceParameter("props", props);
            layer.StartAnimation(nameof(Visual.Size), layerSizeAnim);

            ElementCompositionPreview.SetElementChildVisual(host, layer);
            // XAML Opacity=0 会剔除源子树，因此在合成层隐藏 PART_Original 以保留 PART_Content 的正常输出。
            var originalVisual = ElementCompositionPreview.GetElementVisual(original);
            originalVisual.Opacity = 0;

            _activeHost = host;
            _activeOriginalVisual = originalVisual;
            _resources = resources;
            _props = props;
            _stripes = stripes;
            _layer = layer;
            _redirect = redirect;
            ApplyOutsetPadding();

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

    private void ApplyOutsetPadding()
    {
        if (_props == null || _stripes == null || _layer == null || _redirect == null)
        {
            return;
        }

        var outset = VisualOutset;
        var pad = double.IsFinite(outset) && outset > 0 ? (float)Math.Ceiling(outset) : 0f;
        var stripeStep = 10f / MathF.Sqrt(2f);
        _props.InsertScalar("pad", pad);
        _redirect.Offset = new Vector3(pad, pad, 0f);
        _layer.Offset = new Vector3(-pad, -pad, 0f);
        _stripes.StartPoint = new Vector2(pad, pad);
        _stripes.EndPoint = new Vector2(pad + stripeStep, pad + stripeStep);
    }

    private void ReleaseMask()
    {
        var host = _activeHost;
        var originalVisual = _activeOriginalVisual;
        var resources = _resources;
        _activeHost = null;
        _activeOriginalVisual = null;
        _resources = null;
        _props = null;
        _stripes = null;
        _layer = null;
        _redirect = null;

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
