using System.Numerics;
using System.Reactive.Disposables;
using Microsoft.Graphics.Canvas.Effects;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>在内容后方按实际 Alpha 轮廓投射自定义外阴影的通用装饰容器。</summary>
public sealed class ShadowDecorator : ContentControl, IVisualOutsetProvider
{
    public static readonly DependencyProperty ShadowSpecProperty = DependencyProperty.Register(
        nameof(ShadowSpec),
        typeof(ShadowEffectSpec),
        typeof(ShadowDecorator),
        new PropertyMetadata(null, (sender, _) => ((ShadowDecorator)sender).SyncShadow()));

    public static readonly DependencyProperty SourceElementProperty = DependencyProperty.Register(
        nameof(SourceElement),
        typeof(FrameworkElement),
        typeof(ShadowDecorator),
        new PropertyMetadata(null, (sender, _) => ((ShadowDecorator)sender).SyncShadow()));

    private readonly List<IVisualRedirectSourceProvider> _observedProviders = [];
    private IVisualOutsetProvider? _observedOutsetProvider;
    private FrameworkElement? _shadowHost;
    private FrameworkElement? _contentPresenter;
    private FrameworkElement? _activeHost;
    private FrameworkElement? _activeSource;
    private CompositeDisposable? _resources;
    private CompositionEffectBrush? _brush;
    private CompositionPropertySet? _props;
    private LayerVisual? _layer;
    private RedirectVisual? _redirect;
    private Thickness _visualOutsetThickness;
    private double _visualOutset;
    private bool _isLoaded;

    public ShadowEffectSpec? ShadowSpec
    {
        get => (ShadowEffectSpec?)GetValue(ShadowSpecProperty);
        set => SetValue(ShadowSpecProperty, value);
    }

    /// <summary>可选的轮廓源元素；未指定时使用模板内容区本身。</summary>
    public FrameworkElement? SourceElement
    {
        get => (FrameworkElement?)GetValue(SourceElementProperty);
        set => SetValue(SourceElementProperty, value);
    }

    public double VisualOutset => _visualOutset;

    public Thickness VisualOutsetThickness => _visualOutsetThickness;

    public event EventHandler? VisualOutsetChanged;

    public ShadowDecorator()
    {
        DefaultStyleKey = typeof(ShadowDecorator);
        RegisterPropertyChangedCallback(ContentProperty, (_, _) => SyncShadow());
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ReleaseShadowVisualsOnly();
        _shadowHost = GetTemplateChild("PART_ShadowHost") as FrameworkElement;
        _contentPresenter = GetTemplateChild("PART_Content") as FrameworkElement;
        SyncShadow();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = true;
        SyncShadow();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded)
        {
            _isLoaded = true;
            SyncShadow();
            return;
        }

        _isLoaded = false;
        ReleaseShadow();
    }

    private void SyncShadow()
    {
        if (!_isLoaded)
        {
            ReleaseShadow();
            return;
        }

        var effectiveSource = ResolveEffectiveSource();
        if (ShadowSpec is not { } spec
            || _shadowHost is not { } host
            || !CompositionCapabilities.GetForCurrentView().AreEffectsSupported()
            || effectiveSource is not { } source)
        {
            ReleaseShadowVisualsOnly();
            SetVisualOutset(OpacityEffectHelper.NormalizeOutset(_observedOutsetProvider?.VisualOutsetThickness ?? default));
            return;
        }

        if (!ReferenceEquals(_activeHost, host) || !ReferenceEquals(_activeSource, source) || _layer == null)
        {
            ReleaseShadowVisualsOnly();
            if (!CreateShadowVisuals(host, source))
            {
                SetVisualOutset(OpacityEffectHelper.NormalizeOutset(_observedOutsetProvider?.VisualOutsetThickness ?? default));
                return;
            }
        }

        ApplySpec(spec);
    }

    private void OnRedirectSourceChanged(object? sender, EventArgs args) => SyncShadow();

    private void OnChildVisualOutsetChanged(object? sender, EventArgs args) => SyncShadow();

    private FrameworkElement? ResolveEffectiveSource()
    {
        ReleaseObservedProviders();
        if ((SourceElement ?? Content) is IVisualOutsetProvider outsetProvider)
        {
            _observedOutsetProvider = outsetProvider;
            outsetProvider.VisualOutsetChanged += OnChildVisualOutsetChanged;
        }

        if (SourceElement != null)
        {
            return SourceElement;
        }

        if (Content is not IVisualRedirectSourceProvider)
        {
            return _contentPresenter;
        }

        object? current = Content;
        while (current is IVisualRedirectSourceProvider provider)
        {
            if (_observedProviders.Contains(provider))
            {
                return null;
            }

            _observedProviders.Add(provider);
            provider.VisualRedirectSourceChanged += OnRedirectSourceChanged;
            current = provider.VisualRedirectSource;
        }

        return current as FrameworkElement;
    }

    private void ReleaseObservedProviders()
    {
        if (_observedOutsetProvider != null)
        {
            _observedOutsetProvider.VisualOutsetChanged -= OnChildVisualOutsetChanged;
            _observedOutsetProvider = null;
        }

        if (_observedProviders.Count == 0)
        {
            return;
        }

        foreach (var provider in _observedProviders)
        {
            provider.VisualRedirectSourceChanged -= OnRedirectSourceChanged;
        }

        _observedProviders.Clear();
    }

    private bool CreateShadowVisuals(FrameworkElement host, FrameworkElement source)
    {
        var resources = new CompositeDisposable();
        try
        {
            var sourceVisual = ElementCompositionPreview.GetElementVisual(source);
            var compositor = sourceVisual.Compositor;

            using var colorSource = new ColorSourceEffect
            {
                Name = "ShadowColor",
                Color = Colors.Black
            };
            using var alphaMask = new AlphaMaskEffect
            {
                Source = colorSource,
                // 未绑定的 Content 参数在 LayerVisual 上接收子树（RedirectVisual）实时渲染输出。
                AlphaMask = new CompositionEffectSourceParameter("Content")
            };
            using var blur = new GaussianBlurEffect
            {
                Name = "ShadowBlur",
                Source = alphaMask,
                BlurAmount = 0f,
                BorderMode = EffectBorderMode.Soft,
                Optimization = EffectOptimization.Balanced
            };

            var factory = compositor.CreateEffectFactory(blur, ["ShadowColor.Color", "ShadowBlur.BlurAmount"]);
            resources.Add(factory);
            var brush = factory.CreateBrush();
            resources.Add(brush);

            var props = compositor.CreatePropertySet();
            resources.Add(props);
            props.InsertScalar("pad", 0f);

            var layer = compositor.CreateLayerVisual();
            resources.Add(layer);
            layer.Effect = brush;

            var redirect = compositor.CreateRedirectVisual(sourceVisual);
            resources.Add(redirect);
            layer.Children.InsertAtTop(redirect);

            var redirectSizeAnim = compositor.CreateExpressionAnimation("source.Size");
            resources.Add(redirectSizeAnim);
            redirectSizeAnim.SetReferenceParameter("source", sourceVisual);
            redirect.StartAnimation(nameof(Visual.Size), redirectSizeAnim);

            var layerSizeAnim = compositor.CreateExpressionAnimation(
                "source.Size.X > 0 && source.Size.Y > 0 ? source.Size + Vector2(props.pad * 2, props.pad * 2) : Vector2(0, 0)");
            resources.Add(layerSizeAnim);
            layerSizeAnim.SetReferenceParameter("source", sourceVisual);
            layerSizeAnim.SetReferenceParameter("props", props);
            layer.StartAnimation(nameof(Visual.Size), layerSizeAnim);

            ElementCompositionPreview.SetElementChildVisual(host, layer);

            _activeHost = host;
            _activeSource = source;
            _resources = resources;
            _brush = brush;
            _props = props;
            _layer = layer;
            _redirect = redirect;
            return true;
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }

    private void ApplySpec(ShadowEffectSpec spec)
    {
        if (_layer == null || _brush == null || _props == null || _redirect == null)
        {
            return;
        }

        var childThickness = OpacityEffectHelper.NormalizeOutset(_observedOutsetProvider?.VisualOutsetThickness ?? default);
        var opacity = double.IsFinite(spec.Opacity) ? spec.Opacity : 0;
        var effectiveOpacity = (float)Math.Clamp((spec.Color.A / 255.0) * opacity, 0, 1);
        if (effectiveOpacity <= 0f)
        {
            _layer.IsVisible = false;
            SetVisualOutset(childThickness);
            return;
        }

        _layer.IsVisible = true;
        // 阴影着色保持全不透明并交由 LayerVisual.Opacity 合成，避免低透明度下 8-bit 模糊产生色阶断层。
        _layer.Opacity = effectiveOpacity;
        _brush.Properties.InsertColor("ShadowColor.Color", Color.FromArgb(255, spec.Color.R, spec.Color.G, spec.Color.B));

        var blurRadius = double.IsFinite(spec.BlurRadius) ? (float)Math.Clamp(spec.BlurRadius, 0, 100) : 0f;
        _brush.Properties.InsertScalar("ShadowBlur.BlurAmount", blurRadius / 3f);

        // 为软边界高斯模糊及子级描边外扩预留四周边距，防止 LayerVisual 尺寸边界硬裁剪。
        var childOutset = OpacityEffectHelper.GetMaxOutset(childThickness);
        var basePad = blurRadius > 0f ? MathF.Ceiling(blurRadius) + 2f : 0f;
        var outsetPad = childOutset > 0 ? (float)Math.Ceiling(childOutset) : 0f;
        var pad = basePad + outsetPad;
        _props.InsertScalar("pad", pad);
        _redirect.Offset = new Vector3(pad, pad, 0f);

        var offsetX = double.IsFinite(spec.OffsetX) ? (float)spec.OffsetX : 0f;
        var offsetY = double.IsFinite(spec.OffsetY) ? (float)spec.OffsetY : 0f;
        _layer.Offset = new Vector3(offsetX - pad, offsetY - pad, 0f);

        var combined = new Thickness(
            Math.Max(childThickness.Left, Math.Max(0, pad - offsetX)),
            Math.Max(childThickness.Top, Math.Max(0, pad - offsetY)),
            Math.Max(childThickness.Right, Math.Max(0, pad + offsetX)),
            Math.Max(childThickness.Bottom, Math.Max(0, pad + offsetY)));
        SetVisualOutset(OpacityEffectHelper.NormalizeOutset(combined));
    }

    private void ReleaseShadow()
    {
        ReleaseObservedProviders();
        ReleaseShadowVisualsOnly();
        SetVisualOutset(default);
    }

    private void SetVisualOutset(Thickness value)
    {
        if (_visualOutsetThickness == value)
        {
            return;
        }

        _visualOutsetThickness = value;
        _visualOutset = OpacityEffectHelper.GetMaxOutset(value);
        VisualOutsetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReleaseShadowVisualsOnly()
    {
        var host = _activeHost;
        var resources = _resources;
        _activeHost = null;
        _activeSource = null;
        _resources = null;
        _brush = null;
        _props = null;
        _layer = null;
        _redirect = null;

        if (host != null)
        {
            try
            {
                ElementCompositionPreview.SetElementChildVisual(host, null);
            }
            catch
            {
                // 视觉树销毁期间宿主可能已脱离当前合成器。
            }
        }

        resources?.Dispose();
    }
}
