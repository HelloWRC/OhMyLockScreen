using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reactive.Disposables;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Foundation.Metadata;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>对整组子树内容应用统一不透明度与硬件混合方式的可复用装饰容器。</summary>
public sealed class OpacityDecorator : ContentControl, IVisualOutsetProvider
{
    public static readonly DependencyProperty OpacitySpecProperty = DependencyProperty.Register(
        nameof(OpacitySpec),
        typeof(OpacityEffectSpec),
        typeof(OpacityDecorator),
        new PropertyMetadata(null, (sender, _) => ((OpacityDecorator)sender).SyncOpacity()));

    public static readonly DependencyProperty SourceElementProperty = DependencyProperty.Register(
        nameof(SourceElement),
        typeof(FrameworkElement),
        typeof(OpacityDecorator),
        new PropertyMetadata(null, (sender, _) => ((OpacityDecorator)sender).OnSourceElementChanged()));

    [SupportedOSPlatformGuard("windows10.0.18362")]
    [UnconditionalSuppressMessage("Interoperability", "CA1416", Justification = "The runtime API guard supports Windows 10 17763 targets without VisualSurface.")]
    private static bool IsVisualSurfaceSupported => ApiInformation.IsMethodPresent(
        "Windows.UI.Composition.Compositor", nameof(Compositor.CreateVisualSurface));

    [SupportedOSPlatformGuard("windows10.0.18362")]
    [UnconditionalSuppressMessage("Interoperability", "CA1416", Justification = "The runtime API guard protects XamlRoot access on Windows 10 17763 targets.")]
    private static bool IsXamlRootSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362);

    private static readonly ConditionalWeakTable<Compositor, EffectFactoryCache> FactoryCaches = new();

    private FrameworkElement? _original;
    private FrameworkElement? _contentPresenter;
    private FrameworkElement? _blendHost;
    private IVisualOutsetProvider? _observedOutsetProvider;
    private XamlRoot? _observedXamlRoot;
    private Visual? _hiddenOriginalVisual;
    private PipelineKey? _activeKey;
    private CompositeDisposable? _resources;
    private CompositionPropertySet? _props;
    private CompositionVisualSurface? _contentSurface;
    private SpriteVisual? _spriteVisual;
    private Thickness _appliedOutset = new(double.NaN);
    private Thickness _visualOutsetThickness;
    private double _visualOutset;
    private bool _isLoaded;

    public OpacityEffectSpec? OpacitySpec
    {
        get => (OpacityEffectSpec?)GetValue(OpacitySpecProperty);
        set => SetValue(OpacitySpecProperty, value);
    }

    /// <summary>可选的渲染源元素；未指定时使用模板内容区本身。</summary>
    public FrameworkElement? SourceElement
    {
        get => (FrameworkElement?)GetValue(SourceElementProperty);
        set => SetValue(SourceElementProperty, value);
    }

    public double VisualOutset => _visualOutset;

    public Thickness VisualOutsetThickness => _visualOutsetThickness;

    public event EventHandler? VisualOutsetChanged;

    public OpacityDecorator()
    {
        DefaultStyleKey = typeof(OpacityDecorator);
        RegisterPropertyChangedCallback(ContentProperty, (_, _) =>
        {
            UpdateObservedOutsetProvider();
            SyncOpacity();
        });
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ReleaseBlendVisualsOnly();
        _original = GetTemplateChild("PART_Original") as FrameworkElement;
        _contentPresenter = GetTemplateChild("PART_Content") as FrameworkElement;
        _blendHost = GetTemplateChild("PART_BlendHost") as FrameworkElement;
        UpdateObservedOutsetProvider();
        SyncOpacity();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = true;
        UpdateObservedXamlRoot();
        UpdateObservedOutsetProvider();
        SyncOpacity();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded)
        {
            _isLoaded = true;
            UpdateObservedXamlRoot();
            UpdateObservedOutsetProvider();
            SyncOpacity();
            return;
        }

        _isLoaded = false;
        ReleaseObservedXamlRoot();
        ReleaseObservedOutsetProvider();
        ReleaseBlend();
    }

    private void OnSourceElementChanged()
    {
        UpdateObservedOutsetProvider();
        SyncOpacity();
    }

    private void UpdateObservedOutsetProvider()
    {
        var next = _isLoaded ? (SourceElement ?? Content) as IVisualOutsetProvider : null;
        if (!ReferenceEquals(_observedOutsetProvider, next))
        {
            if (_observedOutsetProvider != null)
            {
                _observedOutsetProvider.VisualOutsetChanged -= OnChildVisualOutsetChanged;
            }

            _observedOutsetProvider = next;
            if (next != null)
            {
                next.VisualOutsetChanged += OnChildVisualOutsetChanged;
            }
        }

        UpdateForwardedOutset();
    }

    private void ReleaseObservedOutsetProvider()
    {
        if (_observedOutsetProvider != null)
        {
            _observedOutsetProvider.VisualOutsetChanged -= OnChildVisualOutsetChanged;
            _observedOutsetProvider = null;
        }

        SetVisualOutset(default);
    }

    private void OnChildVisualOutsetChanged(object? sender, EventArgs args)
    {
        UpdateForwardedOutset();
        SyncOpacity();
    }

    private void UpdateForwardedOutset()
    {
        var next = _isLoaded
            ? OpacityEffectHelper.NormalizeOutset(_observedOutsetProvider?.VisualOutsetThickness ?? default)
            : default;
        SetVisualOutset(next);
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

    private void SyncOpacity()
    {
        if (!_isLoaded || _original is not { } original || _blendHost is not { } blendHost)
        {
            ReleaseBlendVisualsOnly();
            return;
        }

        if (OpacitySpec is not { } spec)
        {
            ReleaseBlendVisualsOnly();
            SetOriginalXamlOpacity(original, 1.0);
            return;
        }

        var opacity = OpacityEffectHelper.SanitizeOpacity(spec.Opacity, 1.0);
        var blendMode = OpacityEffectHelper.SanitizeBlendMode(spec.BlendMode);

        var needsSurface = opacity > 0 && (blendMode != OpacityBlendMode.Normal || opacity < 1.0);
        var isBlendMode = blendMode != OpacityBlendMode.Normal;
        BlendEffectMode win2DMode = default;
        if (isBlendMode && !TryToWin2DBlendMode(blendMode, out win2DMode))
        {
            isBlendMode = false;
            blendMode = OpacityBlendMode.Normal;
            needsSurface = opacity > 0 && opacity < 1.0;
        }

        if (!needsSurface
            || !IsVisualSurfaceSupported
            || (isBlendMode && !CompositionCapabilities.GetForCurrentView().AreEffectsSupported()))
        {
            ReleaseBlendVisualsOnly();
            SetOriginalXamlOpacity(original, opacity);
            return;
        }

        var source = SourceElement ?? _contentPresenter;
        if (source == null)
        {
            ReleaseBlendVisualsOnly();
            SetOriginalXamlOpacity(original, opacity);
            return;
        }

        var outset = OpacityEffectHelper.NormalizeOutset(_observedOutsetProvider?.VisualOutsetThickness ?? default);
        var effectiveScale = (float)GetRasterizationScale();
        var key = new PipelineKey(blendHost, source, blendMode, effectiveScale);

        // XAML Opacity=0 会剔除源子树，离屏编组与混合路径均必须在合成层隐藏 PART_Original。
        SetOriginalXamlOpacity(original, 1.0);

        if (!Equals(_activeKey, key) || _resources == null)
        {
            ReleaseBlendVisualsOnly();
            if (!CreateSurfaceVisuals(key, isBlendMode ? (BlendEffectMode?)win2DMode : null, original))
            {
                SetOriginalXamlOpacity(original, opacity);
                return;
            }
        }

        ApplyParameters(key, opacity, outset);
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private bool CreateSurfaceVisuals(PipelineKey key, BlendEffectMode? win2DMode, FrameworkElement original)
    {
        var resources = new CompositeDisposable();
        try
        {
            var sourceVisual = ElementCompositionPreview.GetElementVisual(key.Source);
            var compositor = sourceVisual.Compositor;

            var props = compositor.CreatePropertySet();
            resources.Add(props);
            props.InsertScalar("padL", 0f);
            props.InsertScalar("padT", 0f);
            props.InsertScalar("padR", 0f);
            props.InsertScalar("padB", 0f);
            props.InsertScalar("scale", key.EffectiveScale);

            var surfaceSizeAnim = compositor.CreateExpressionAnimation(
                "source.Size.X > 0 && source.Size.Y > 0 ? (source.Size + Vector2(props.padL + props.padR, props.padT + props.padB)) * props.scale : Vector2(0, 0)");
            resources.Add(surfaceSizeAnim);
            surfaceSizeAnim.SetReferenceParameter("source", sourceVisual);
            surfaceSizeAnim.SetReferenceParameter("props", props);

            var logicalSizeAnim = compositor.CreateExpressionAnimation(
                "source.Size.X > 0 && source.Size.Y > 0 ? source.Size + Vector2(props.padL + props.padR, props.padT + props.padB) : Vector2(0, 0)");
            resources.Add(logicalSizeAnim);
            logicalSizeAnim.SetReferenceParameter("source", sourceVisual);
            logicalSizeAnim.SetReferenceParameter("props", props);

            var contentSurface = compositor.CreateVisualSurface();
            resources.Add(contentSurface);
            contentSurface.SourceOffset = Vector2.Zero;

            if (key.EffectiveScale > 1f)
            {
                var scaleContainer = compositor.CreateContainerVisual();
                resources.Add(scaleContainer);

                var contentRedirect = compositor.CreateRedirectVisual(sourceVisual);
                resources.Add(contentRedirect);
                contentRedirect.Scale = new Vector3(key.EffectiveScale, key.EffectiveScale, 1f);
                var redirectSizeAnim = compositor.CreateExpressionAnimation("source.Size");
                resources.Add(redirectSizeAnim);
                redirectSizeAnim.SetReferenceParameter("source", sourceVisual);
                contentRedirect.StartAnimation(nameof(Visual.Size), redirectSizeAnim);
                scaleContainer.Children.InsertAtTop(contentRedirect);

                contentSurface.SourceVisual = scaleContainer;
            }
            else
            {
                contentSurface.SourceVisual = sourceVisual;
            }

            contentSurface.StartAnimation(nameof(CompositionVisualSurface.SourceSize), surfaceSizeAnim);

            var contentSurfaceBrush = compositor.CreateSurfaceBrush(contentSurface);
            resources.Add(contentSurfaceBrush);
            contentSurfaceBrush.Stretch = CompositionStretch.Fill;
            contentSurfaceBrush.HorizontalAlignmentRatio = 0.5f;
            contentSurfaceBrush.VerticalAlignmentRatio = 0.5f;
            contentSurfaceBrush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;

            CompositionBrush outputBrush;
            if (win2DMode is { } mode)
            {
                var cache = FactoryCaches.GetValue(compositor, static c => new EffectFactoryCache(c));
                var backdropBrush = compositor.CreateBackdropBrush();
                resources.Add(backdropBrush);

                var blendBrush = cache.GetFactory(mode).CreateBrush();
                resources.Add(blendBrush);
                blendBrush.SetSourceParameter("Backdrop0", backdropBrush);
                blendBrush.SetSourceParameter("Backdrop1", backdropBrush);
                blendBrush.SetSourceParameter("Content0", contentSurfaceBrush);
                blendBrush.SetSourceParameter("Content1", contentSurfaceBrush);
                outputBrush = blendBrush;
            }
            else
            {
                // Normal 模式下直接将离屏表面作为统一编组绘制，避免阴影及子控件互透。
                outputBrush = contentSurfaceBrush;
            }

            var sprite = compositor.CreateSpriteVisual();
            resources.Add(sprite);
            sprite.Brush = outputBrush;
            sprite.Scale = Vector3.One;
            sprite.StartAnimation(nameof(Visual.Size), logicalSizeAnim);

            var originalVisual = ElementCompositionPreview.GetElementVisual(original);
            originalVisual.Opacity = 0f;
            _hiddenOriginalVisual = originalVisual;

            ElementCompositionPreview.SetElementChildVisual(key.Host, sprite);

            _activeKey = key;
            _resources = resources;
            _props = props;
            _contentSurface = contentSurface;
            _spriteVisual = sprite;
            _appliedOutset = new Thickness(double.NaN);
            return true;
        }
        catch
        {
            RestoreOriginalVisualVisibility();
            resources.Dispose();
            throw;
        }
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private void ApplyParameters(PipelineKey key, double opacity, Thickness outset)
    {
        if (_props == null || _contentSurface == null || _spriteVisual == null)
        {
            return;
        }

        _spriteVisual.Opacity = (float)opacity;

        if (_appliedOutset != outset)
        {
            _appliedOutset = outset;
            var padL = (float)outset.Left;
            var padT = (float)outset.Top;
            var padR = (float)outset.Right;
            var padB = (float)outset.Bottom;

            _props.InsertScalar("padL", padL);
            _props.InsertScalar("padT", padT);
            _props.InsertScalar("padR", padR);
            _props.InsertScalar("padB", padB);
            _contentSurface.SourceOffset = new Vector2(-padL * key.EffectiveScale, -padT * key.EffectiveScale);
            _spriteVisual.Offset = new Vector3(-padL, -padT, 0f);
        }
    }

    private void ReleaseBlend()
    {
        ReleaseBlendVisualsOnly();
        if (_original != null)
        {
            SetOriginalXamlOpacity(_original, 1.0);
        }
    }

    private void ReleaseBlendVisualsOnly()
    {
        var host = _activeKey?.Host;
        var resources = _resources;
        _activeKey = null;
        _resources = null;
        _props = null;
        _contentSurface = null;
        _spriteVisual = null;
        _appliedOutset = new Thickness(double.NaN);

        RestoreOriginalVisualVisibility();

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

    private static void SetOriginalXamlOpacity(FrameworkElement original, double opacity)
    {
        if (original.Opacity != opacity)
        {
            original.Opacity = opacity;
        }
    }

    private void RestoreOriginalVisualVisibility()
    {
        if (_hiddenOriginalVisual == null)
        {
            return;
        }

        try
        {
            _hiddenOriginalVisual.Opacity = 1f;
        }
        catch
        {
        }

        _hiddenOriginalVisual = null;
    }

    private void UpdateObservedXamlRoot()
    {
        if (!IsXamlRootSupported)
        {
            return;
        }

        var current = XamlRoot;
        if (ReferenceEquals(_observedXamlRoot, current))
        {
            return;
        }

        ReleaseObservedXamlRoot();
        _observedXamlRoot = current;
        if (current != null)
        {
            current.Changed += OnXamlRootChanged;
        }
    }

    private void ReleaseObservedXamlRoot()
    {
        if (!IsXamlRootSupported || _observedXamlRoot == null)
        {
            _observedXamlRoot = null;
            return;
        }

        _observedXamlRoot.Changed -= OnXamlRootChanged;
        _observedXamlRoot = null;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => SyncOpacity();

    private double GetRasterizationScale()
    {
        if (!IsXamlRootSupported)
        {
            return 1.0;
        }

        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        return double.IsFinite(scale) && scale > 0 ? scale : 1.0;
    }

    private static bool TryToWin2DBlendMode(OpacityBlendMode mode, out BlendEffectMode win2DMode)
    {
        switch (mode)
        {
            case OpacityBlendMode.Darken:
                win2DMode = BlendEffectMode.Darken;
                return true;
            case OpacityBlendMode.Multiply:
                win2DMode = BlendEffectMode.Multiply;
                return true;
            case OpacityBlendMode.ColorBurn:
                win2DMode = BlendEffectMode.ColorBurn;
                return true;
            case OpacityBlendMode.LinearBurn:
                win2DMode = BlendEffectMode.LinearBurn;
                return true;
            case OpacityBlendMode.DarkerColor:
                win2DMode = BlendEffectMode.DarkerColor;
                return true;
            case OpacityBlendMode.Lighten:
                win2DMode = BlendEffectMode.Lighten;
                return true;
            case OpacityBlendMode.Screen:
                win2DMode = BlendEffectMode.Screen;
                return true;
            case OpacityBlendMode.ColorDodge:
                win2DMode = BlendEffectMode.ColorDodge;
                return true;
            case OpacityBlendMode.LinearDodge:
                win2DMode = BlendEffectMode.LinearDodge;
                return true;
            case OpacityBlendMode.LighterColor:
                win2DMode = BlendEffectMode.LighterColor;
                return true;
            case OpacityBlendMode.Overlay:
                win2DMode = BlendEffectMode.Overlay;
                return true;
            case OpacityBlendMode.SoftLight:
                win2DMode = BlendEffectMode.SoftLight;
                return true;
            case OpacityBlendMode.HardLight:
                win2DMode = BlendEffectMode.HardLight;
                return true;
            case OpacityBlendMode.VividLight:
                win2DMode = BlendEffectMode.VividLight;
                return true;
            case OpacityBlendMode.LinearLight:
                win2DMode = BlendEffectMode.LinearLight;
                return true;
            case OpacityBlendMode.PinLight:
                win2DMode = BlendEffectMode.PinLight;
                return true;
            case OpacityBlendMode.HardMix:
                win2DMode = BlendEffectMode.HardMix;
                return true;
            case OpacityBlendMode.Difference:
                win2DMode = BlendEffectMode.Difference;
                return true;
            case OpacityBlendMode.Exclusion:
                win2DMode = BlendEffectMode.Exclusion;
                return true;
            case OpacityBlendMode.Subtract:
                win2DMode = BlendEffectMode.Subtract;
                return true;
            case OpacityBlendMode.Division:
                win2DMode = BlendEffectMode.Division;
                return true;
            default:
                win2DMode = default;
                return false;
        }
    }

    private sealed record PipelineKey(
        FrameworkElement Host,
        FrameworkElement Source,
        OpacityBlendMode BlendMode,
        float EffectiveScale);

    /// <summary>按 Compositor 与混合方式懒编译并缓存 Alpha 保持型混合效果工厂。</summary>
    private sealed class EffectFactoryCache(Compositor compositor)
    {
        private readonly Compositor _compositor = compositor;
        private readonly Dictionary<BlendEffectMode, CompositionEffectFactory> _factories = [];

        public CompositionEffectFactory GetFactory(BlendEffectMode mode)
        {
            if (_factories.TryGetValue(mode, out var existing))
            {
                return existing;
            }

            var created = CreateFactory(mode);
            _factories.Add(mode, created);
            return created;
        }

        private CompositionEffectFactory CreateFactory(BlendEffectMode mode)
        {
            using var nodes = new CompositeDisposable();
            // Direct2D BlendEffect 在 Composition 管线中将 Background 作为前景运算端、Foreground 作为底图端；
            // 对调赋值后全部 21 种混合模式均与 Photoshop / Figma / W3C mix-blend-mode 完全一致。
            var blend = new BlendEffect
            {
                Mode = mode,
                Background = new CompositionEffectSourceParameter("Content0"),
                Foreground = new CompositionEffectSourceParameter("Backdrop0")
            };
            nodes.Add(blend);

            var destOut = new CompositeEffect
            {
                Mode = CanvasComposite.DestinationOut,
                Sources =
                {
                    new CompositionEffectSourceParameter("Backdrop1"),
                    new CompositionEffectSourceParameter("Content1")
                }
            };
            nodes.Add(destOut);

            // 仅输出前景轮廓内的混合贡献并保留原始前景 Alpha，避免半透明背景上包围盒重影与不透明度异常叠加。
            var root = new ArithmeticCompositeEffect
            {
                Source1 = blend,
                Source2 = destOut,
                Source1Amount = 1f,
                Source2Amount = -1f,
                MultiplyAmount = 0f,
                Offset = 0f,
                ClampOutput = true
            };
            nodes.Add(root);

            return _compositor.CreateEffectFactory(root);
        }
    }
}
