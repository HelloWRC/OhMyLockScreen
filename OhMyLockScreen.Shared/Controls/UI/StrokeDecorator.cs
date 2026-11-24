using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reactive.Disposables;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Foundation.Metadata;
using Windows.Graphics.Effects;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>在内容轮廓的内、中、外侧绘制通用 XAML 画刷描边（支持镂空）的通用装饰容器。</summary>
public sealed class StrokeDecorator : ContentControl, IVisualOutsetProvider
{
    public static readonly DependencyProperty StrokeSpecProperty = DependencyProperty.Register(
        nameof(StrokeSpec),
        typeof(StrokeEffectSpec),
        typeof(StrokeDecorator),
        new PropertyMetadata(null, (sender, _) => ((StrokeDecorator)sender).SyncStroke()));

    public static readonly DependencyProperty SourceElementProperty = DependencyProperty.Register(
        nameof(SourceElement),
        typeof(FrameworkElement),
        typeof(StrokeDecorator),
        new PropertyMetadata(null, (sender, _) => ((StrokeDecorator)sender).SyncStroke()));

    private const float MaxThickness = 64f;

    [SupportedOSPlatformGuard("windows10.0.18362")]
    [UnconditionalSuppressMessage("Interoperability", "CA1416", Justification = "The runtime API guard supports Windows 10 17763 targets without VisualSurface.")]
    private static bool IsVisualSurfaceSupported => ApiInformation.IsMethodPresent(
        "Windows.UI.Composition.Compositor", nameof(Compositor.CreateVisualSurface));

    [SupportedOSPlatformGuard("windows10.0.18362")]
    [UnconditionalSuppressMessage("Interoperability", "CA1416", Justification = "The runtime API guard protects XamlRoot access on Windows 10 17763 targets.")]
    private static bool IsXamlRootSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362);

    private static readonly ConditionalWeakTable<Compositor, EffectFactoryCache> FactoryCaches = new();
    private static readonly StrokeMorphologyPlan EmptyPlan = new(false, Array.Empty<StrokeMorphologyPass>());

    private static readonly Matrix5x4 PremultipliedAlphaToWhiteMatrix = new()
    {
        M41 = 1f,
        M42 = 1f,
        M43 = 1f,
        M44 = 1f
    };

    private static readonly Matrix5x4 AlphaToOpaqueGrayMatrix = new()
    {
        M41 = 1f,
        M42 = 1f,
        M43 = 1f,
        M54 = 1f
    };

    private static readonly Matrix5x4 OpaqueGrayToAlphaMaskMatrix = new()
    {
        M11 = 1f,
        M22 = 1f,
        M33 = 1f,
        M14 = 1f
    };

    private readonly List<IVisualRedirectSourceProvider> _observedProviders = [];
    private readonly List<CompositionEffectBrush> _outerStages = [];
    private readonly List<CompositionEffectBrush> _innerStages = [];
    private Canvas? _brushContainer;
    private Border? _brushSurface;
    private FrameworkElement? _backHost;
    private FrameworkElement? _original;
    private FrameworkElement? _contentPresenter;
    private FrameworkElement? _frontHost;
    private FrameworkElement? _observedSource;
    private XamlRoot? _observedXamlRoot;
    private SolidColorBrush? _observedSolidBrush;
    private long _solidColorCallbackToken;
    private long _solidOpacityCallbackToken;
    private Visual? _hiddenOriginalVisual;
    private Visual? _hiddenBrushContainerVisual;
    private PipelineKey? _activeKey;
    private CompositeDisposable? _resources;
    private CompositionPropertySet? _props;
    private CompositionVisualSurface? _contentSurface;
    private CompositionVisualSurface? _brushSurfaceObj;
    private CompositionColorBrush? _solidStrokeBrush;
    private Visual? _activeOutputVisual;
    private Visual? _brushRedirectVisual;
    private float _activeSurfacePad = float.NaN;
    private float _activeBrushPad = float.NaN;
    private double _appliedBrushWidth = double.NaN;
    private double _appliedBrushHeight = double.NaN;
    private double _appliedBrushLeft = double.NaN;
    private double _appliedBrushTop = double.NaN;
    private bool _isLoaded;
    private double _visualOutset;

    public StrokeEffectSpec? StrokeSpec
    {
        get => (StrokeEffectSpec?)GetValue(StrokeSpecProperty);
        set => SetValue(StrokeSpecProperty, value);
    }

    /// <summary>可选的轮廓源元素；未指定时使用模板内容区本身。</summary>
    public FrameworkElement? SourceElement
    {
        get => (FrameworkElement?)GetValue(SourceElementProperty);
        set => SetValue(SourceElementProperty, value);
    }

    public double VisualOutset => _visualOutset;

    public event EventHandler? VisualOutsetChanged;

    public StrokeDecorator()
    {
        DefaultStyleKey = typeof(StrokeDecorator);
        RegisterPropertyChangedCallback(ContentProperty, (_, _) => SyncStroke());
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ReleaseStroke();
        _brushContainer = GetTemplateChild("PART_BrushContainer") as Canvas;
        _brushSurface = GetTemplateChild("PART_BrushSurface") as Border;
        _backHost = GetTemplateChild("PART_BackHost") as FrameworkElement;
        _original = GetTemplateChild("PART_Original") as FrameworkElement;
        _contentPresenter = GetTemplateChild("PART_Content") as FrameworkElement;
        _frontHost = GetTemplateChild("PART_FrontHost") as FrameworkElement;
        _appliedBrushWidth = double.NaN;
        _appliedBrushHeight = double.NaN;
        _appliedBrushLeft = double.NaN;
        _appliedBrushTop = double.NaN;
        SyncStroke();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = true;
        UpdateObservedXamlRoot();
        SyncStroke();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded)
        {
            _isLoaded = true;
            UpdateObservedXamlRoot();
            SyncStroke();
            return;
        }

        _isLoaded = false;
        ReleaseObservedXamlRoot();
        ReleaseStroke();
    }

    private void SyncStroke()
    {
        if (StrokeSpec is not { } spec
            || !_isLoaded
            || _brushContainer is not { } brushContainer
            || _brushSurface is not { } brushSurface
            || _backHost is not { } backHost
            || _original is not { } original
            || _frontHost is not { } frontHost
            || !IsVisualSurfaceSupported
            || !CompositionCapabilities.GetForCurrentView().AreEffectsSupported())
        {
            ReleaseStroke();
            return;
        }

        var effectiveSource = ResolveEffectiveSource();
        if (effectiveSource is not { } source)
        {
            ReleaseObservedSolidBrush();
            ReleaseStrokeVisualsOnly();
            RestoreOriginalVisibility();
            SetVisualOutset(0);
            return;
        }

        var thickness = double.IsFinite(spec.Thickness) ? Math.Clamp(spec.Thickness, 0, MaxThickness) : 0;
        if (thickness <= 0 || spec.Brush == null)
        {
            ReleaseObservedSolidBrush();
            ReleaseStrokeVisualsOnly();
            ClearBrushSurface(brushSurface);
            SetVisualOutset(0);
            UpdateOriginalVisibility(original, hideOriginal: spec.IsHollow);
            return;
        }

        var (outerRadius, innerRadius) = StrokeMorphologyPlanner.GetEffectiveRadii(thickness, spec.Position);
        var outset = outerRadius > 0 ? Math.Ceiling(outerRadius) + 2 : 0;
        var surfacePad = StrokeMorphologyPlanner.GetSurfacePad(outerRadius);
        var brushMode = spec.Brush switch
        {
            SolidColorBrush => BrushMode.SolidColor,
            AcrylicBrush => BrushMode.Acrylic,
            _ => BrushMode.VisualSurface
        };
        var isAcrylicBrush = brushMode == BrushMode.Acrylic;
        var brushPad = brushMode == BrushMode.Acrylic ? surfacePad : (float)outset;
        var rasterizationScale = GetRasterizationScale();
        var effectiveScale = (float)rasterizationScale;

        var outerPlan = outerRadius > 0
            ? StrokeMorphologyPlanner.BuildPlan(outerRadius, effectiveScale)
            : EmptyPlan;
        var innerPlan = innerRadius > 0
            ? StrokeMorphologyPlanner.BuildPlan(innerRadius, effectiveScale)
            : EmptyPlan;

        var targetHost = !spec.IsHollow && spec.Position == StrokePosition.Outside ? backHost : frontHost;
        var hideOriginal = spec.IsHollow || (spec.Position == StrokePosition.Inside && !isAcrylicBrush);

        var key = new PipelineKey(
            targetHost,
            source,
            brushMode,
            spec.Position,
            spec.IsHollow,
            outerPlan.Passes.Count,
            outerPlan.UsesHalfResolution,
            innerPlan.Passes.Count,
            innerPlan.UsesHalfResolution,
            effectiveScale);

        if (!Equals(_activeKey, key) || _resources == null)
        {
            ReleaseStrokeVisualsOnly();
            if (!CreateStrokeVisuals(key, brushContainer, brushSurface, surfacePad))
            {
                ReleaseObservedSolidBrush();
                RestoreOriginalVisibility();
                SetVisualOutset(0);
                return;
            }
        }

        ObserveSourceSize(source);
        ObserveSolidBrush(spec.Brush as SolidColorBrush);

        if (brushMode == BrushMode.SolidColor)
        {
            ClearBrushSurface(brushSurface);
            UpdateSolidStrokeColor();
        }
        else
        {
            UpdateBrushSurfaceGeometry(source, brushSurface, brushPad);
            if (!ReferenceEquals(brushSurface.Background, spec.Brush))
            {
                brushSurface.Background = spec.Brush;
            }
        }

        UpdateOriginalVisibility(original, hideOriginal);
        ApplyParameters(key, surfacePad, brushPad, outerPlan.Passes, innerPlan.Passes);
        SetVisualOutset(outset);
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private bool CreateStrokeVisuals(
        PipelineKey key,
        Canvas brushContainer,
        Border brushSurface,
        float surfacePad)
    {
        var resources = new CompositeDisposable();
        try
        {
            var sourceVisual = ElementCompositionPreview.GetElementVisual(key.Source);
            var brushVisual = ElementCompositionPreview.GetElementVisual(brushSurface);
            var compositor = sourceVisual.Compositor;
            var cache = FactoryCaches.GetValue(compositor, c => new EffectFactoryCache(c));

            var props = compositor.CreatePropertySet();
            resources.Add(props);
            props.InsertScalar("pad", surfacePad);
            props.InsertScalar("scale", key.EffectiveScale);

            var fullSizeAnim = compositor.CreateExpressionAnimation(
                "source.Size.X > 0 && source.Size.Y > 0 ? (source.Size + Vector2(props.pad * 2, props.pad * 2)) * props.scale : Vector2(0, 0)");
            resources.Add(fullSizeAnim);
            fullSizeAnim.SetReferenceParameter("source", sourceVisual);
            fullSizeAnim.SetReferenceParameter("props", props);

            ExpressionAnimation? halfSizeAnim = null;
            if (key.OuterHalfRes || key.InnerHalfRes)
            {
                halfSizeAnim = compositor.CreateExpressionAnimation(
                    "source.Size.X > 0 && source.Size.Y > 0 ? (source.Size + Vector2(props.pad * 2, props.pad * 2)) * (props.scale * 0.5) + Vector2(1, 1) : Vector2(0, 0)");
                resources.Add(halfSizeAnim);
                halfSizeAnim.SetReferenceParameter("source", sourceVisual);
                halfSizeAnim.SetReferenceParameter("props", props);
            }

            var contentSurface = compositor.CreateVisualSurface();
            resources.Add(contentSurface);
            contentSurface.SourceOffset = new Vector2(-surfacePad * key.EffectiveScale, -surfacePad * key.EffectiveScale);

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

            contentSurface.StartAnimation(nameof(CompositionVisualSurface.SourceSize), fullSizeAnim);
            var contentSurfaceBrush = CreateAlignedSurfaceBrush(compositor, resources, contentSurface);

            // 阶段 0：直接将 premultiplied Alpha 提取为 (a, a, a, a) 白底遮罩。
            var alphaBrush = cache.AlphaExtract.CreateBrush();
            resources.Add(alphaBrush);
            alphaBrush.SetSourceParameter("Content", contentSurfaceBrush);
            var alphaSurfaceBrush = CreateSurfaceStage(compositor, resources, alphaBrush, fullSizeAnim);

            // 阶段 1..N：线性 K=1 迭代高斯阈值扩散/收缩（大半径内部降半分辨率后再全分辨率抛光）。
            var dSurfaceBrush = BuildMorphologyStages(
                compositor, cache, resources, alphaSurfaceBrush,
                key.OuterPassCount, key.OuterHalfRes, _outerStages, fullSizeAnim, halfSizeAnim);
            var eSurfaceBrush = BuildMorphologyStages(
                compositor, cache, resources, alphaSurfaceBrush,
                key.InnerPassCount, key.InnerHalfRes, _innerStages, fullSizeAnim, halfSizeAnim);

            CompositionBrush? paintBrush = null;
            CompositionColorBrush? solidStrokeBrush = null;
            CompositionVisualSurface? brushSurfaceObj = null;

            if (key.BrushMode == BrushMode.SolidColor)
            {
                solidStrokeBrush = compositor.CreateColorBrush(Colors.Transparent);
                resources.Add(solidStrokeBrush);
                paintBrush = solidStrokeBrush;
            }
            else if (key.BrushMode == BrushMode.VisualSurface)
            {
                brushSurfaceObj = compositor.CreateVisualSurface();
                resources.Add(brushSurfaceObj);
                brushSurfaceObj.SourceOffset = Vector2.Zero;

                if (key.EffectiveScale > 1f)
                {
                    var brushScaleContainer = compositor.CreateContainerVisual();
                    resources.Add(brushScaleContainer);

                    var brushRedirect = compositor.CreateRedirectVisual(brushVisual);
                    resources.Add(brushRedirect);
                    brushRedirect.Scale = new Vector3(key.EffectiveScale, key.EffectiveScale, 1f);
                    var brushRedirectSizeAnim = compositor.CreateExpressionAnimation("brush.Size");
                    resources.Add(brushRedirectSizeAnim);
                    brushRedirectSizeAnim.SetReferenceParameter("brush", brushVisual);
                    brushRedirect.StartAnimation(nameof(Visual.Size), brushRedirectSizeAnim);
                    brushScaleContainer.Children.InsertAtTop(brushRedirect);

                    brushSurfaceObj.SourceVisual = brushScaleContainer;
                }
                else
                {
                    brushSurfaceObj.SourceVisual = brushVisual;
                }

                brushSurfaceObj.StartAnimation(nameof(CompositionVisualSurface.SourceSize), fullSizeAnim);
                paintBrush = CreateAlignedSurfaceBrush(compositor, resources, brushSurfaceObj);
            }

            var finalFactory = cache.GetFinalFactory(key.Position, key.IsHollow, key.BrushMode == BrushMode.Acrylic);
            var finalBrush = finalFactory.CreateBrush();
            resources.Add(finalBrush);

            BindFinalSources(
                compositor, resources, key, finalBrush, contentSurfaceBrush, alphaSurfaceBrush,
                dSurfaceBrush, eSurfaceBrush, paintBrush);

            Visual outputVisual;
            Visual? brushRedirectVisual = null;
            if (key.BrushMode == BrushMode.Acrylic)
            {
                var layer = compositor.CreateLayerVisual();
                resources.Add(layer);
                layer.Effect = finalBrush;

                var brushRedirect = compositor.CreateRedirectVisual(brushVisual);
                resources.Add(brushRedirect);
                var brushRedirectSizeAnim = compositor.CreateExpressionAnimation("brush.Size");
                resources.Add(brushRedirectSizeAnim);
                brushRedirectSizeAnim.SetReferenceParameter("brush", brushVisual);
                brushRedirect.StartAnimation(nameof(Visual.Size), brushRedirectSizeAnim);
                layer.Children.InsertAtTop(brushRedirect);
                brushRedirectVisual = brushRedirect;

                var layerSizeAnim = compositor.CreateExpressionAnimation(
                    "source.Size.X > 0 && source.Size.Y > 0 ? source.Size + Vector2(props.pad * 2, props.pad * 2) : Vector2(0, 0)");
                resources.Add(layerSizeAnim);
                layerSizeAnim.SetReferenceParameter("source", sourceVisual);
                layerSizeAnim.SetReferenceParameter("props", props);
                layer.StartAnimation(nameof(Visual.Size), layerSizeAnim);
                outputVisual = layer;
            }
            else
            {
                var sprite = compositor.CreateSpriteVisual();
                resources.Add(sprite);
                sprite.Brush = finalBrush;
                sprite.Scale = new Vector3(1f / key.EffectiveScale, 1f / key.EffectiveScale, 1f);
                sprite.StartAnimation(nameof(Visual.Size), fullSizeAnim);
                outputVisual = sprite;
            }

            // XAML Opacity=0 会剔除笔刷子树并禁用 Backdrop，因此在合成层隐藏承载容器。
            var brushContainerVisual = ElementCompositionPreview.GetElementVisual(brushContainer);
            brushContainerVisual.Opacity = 0f;
            _hiddenBrushContainerVisual = brushContainerVisual;

            ElementCompositionPreview.SetElementChildVisual(key.Host, outputVisual);

            _activeKey = key;
            _resources = resources;
            _props = props;
            _contentSurface = contentSurface;
            _brushSurfaceObj = brushSurfaceObj;
            _solidStrokeBrush = solidStrokeBrush;
            _activeOutputVisual = outputVisual;
            _brushRedirectVisual = brushRedirectVisual;
            _activeSurfacePad = float.NaN;
            return true;
        }
        catch
        {
            _outerStages.Clear();
            _innerStages.Clear();
            resources.Dispose();
            RestoreBrushContainerVisibility();
            throw;
        }
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private static CompositionSurfaceBrush CreateAlignedSurfaceBrush(
        Compositor compositor,
        CompositeDisposable resources,
        ICompositionSurface surface)
    {
        var brush = compositor.CreateSurfaceBrush(surface);
        resources.Add(brush);
        brush.Stretch = CompositionStretch.None;
        brush.HorizontalAlignmentRatio = 0f;
        brush.VerticalAlignmentRatio = 0f;
        return brush;
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private static CompositionSurfaceBrush CreateFillSurfaceBrush(
        Compositor compositor,
        CompositeDisposable resources,
        ICompositionSurface surface)
    {
        var brush = compositor.CreateSurfaceBrush(surface);
        resources.Add(brush);
        brush.Stretch = CompositionStretch.Fill;
        brush.HorizontalAlignmentRatio = 0.5f;
        brush.VerticalAlignmentRatio = 0.5f;
        brush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;
        return brush;
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private static CompositionSurfaceBrush CreateSurfaceStage(
        Compositor compositor,
        CompositeDisposable resources,
        CompositionBrush stageBrush,
        ExpressionAnimation sizeAnim)
    {
        var sprite = compositor.CreateSpriteVisual();
        resources.Add(sprite);
        sprite.Brush = stageBrush;
        sprite.StartAnimation(nameof(Visual.Size), sizeAnim);

        var surface = compositor.CreateVisualSurface();
        resources.Add(surface);
        surface.SourceVisual = sprite;
        surface.SourceOffset = Vector2.Zero;
        surface.StartAnimation(nameof(CompositionVisualSurface.SourceSize), sizeAnim);

        return CreateAlignedSurfaceBrush(compositor, resources, surface);
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private static CompositionSurfaceBrush BuildMorphologyStages(
        Compositor compositor,
        EffectFactoryCache cache,
        CompositeDisposable resources,
        CompositionSurfaceBrush initialSurface,
        int passCount,
        bool usesHalfRes,
        List<CompositionEffectBrush> stages,
        ExpressionAnimation fullSizeAnim,
        ExpressionAnimation? halfSizeAnim)
    {
        if (passCount <= 0)
        {
            return initialSurface;
        }

        var currentSurface = initialSurface;
        for (var i = 0; i < passCount; i++)
        {
            CompositionEffectFactory factory;
            ExpressionAnimation stageSizeAnim;
            if (!usesHalfRes)
            {
                factory = cache.BlurThreshold;
                stageSizeAnim = fullSizeAnim;
            }
            else if (i == 0)
            {
                factory = cache.BlurThreshold;
                stageSizeAnim = fullSizeAnim;
            }
            else if (i == passCount - 1)
            {
                factory = cache.UpscaleBlurThreshold;
                stageSizeAnim = fullSizeAnim;
            }
            else
            {
                factory = i == 1 ? cache.DownscaleBlurThreshold : cache.BlurThreshold;
                stageSizeAnim = halfSizeAnim!;
            }

            var stageBrush = factory.CreateBrush();
            resources.Add(stageBrush);
            stageBrush.SetSourceParameter("S0", currentSurface);
            stages.Add(stageBrush);
            currentSurface = CreateSurfaceStage(compositor, resources, stageBrush, stageSizeAnim);
        }

        return currentSurface;
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private static void BindFinalSources(
        Compositor compositor,
        CompositeDisposable resources,
        PipelineKey key,
        CompositionEffectBrush finalBrush,
        CompositionSurfaceBrush contentSurfaceBrush,
        CompositionSurfaceBrush alphaSurfaceBrush,
        CompositionSurfaceBrush dSurfaceBrush,
        CompositionSurfaceBrush eSurfaceBrush,
        CompositionBrush? paintBrush)
    {
        var isAcrylic = key.BrushMode == BrushMode.Acrylic;
        CompositionSurfaceBrush WrapFinalMask(CompositionSurfaceBrush sourceBrush)
            => isAcrylic ? CreateFillSurfaceBrush(compositor, resources, sourceBrush.Surface) : sourceBrush;

        if (key.Position == StrokePosition.Outside && !key.IsHollow)
        {
            var finalD = WrapFinalMask(dSurfaceBrush);
            finalBrush.SetSourceParameter("D0", finalD);
            finalBrush.SetSourceParameter("D1", finalD);
            finalBrush.SetSourceParameter("A0", WrapFinalMask(alphaSurfaceBrush));
        }
        else if (key.Position == StrokePosition.Inside && !key.IsHollow && !isAcrylic)
        {
            finalBrush.SetSourceParameter("E0", eSurfaceBrush);
            finalBrush.SetSourceParameter("Content", contentSurfaceBrush);
        }
        else
        {
            var (posSurface, negSurface) = key.Position switch
            {
                StrokePosition.Outside => (dSurfaceBrush, alphaSurfaceBrush),
                StrokePosition.Center => (dSurfaceBrush, eSurfaceBrush),
                StrokePosition.Inside => (alphaSurfaceBrush, eSurfaceBrush),
                _ => (alphaSurfaceBrush, alphaSurfaceBrush)
            };
            finalBrush.SetSourceParameter("Pos", WrapFinalMask(posSurface));
            finalBrush.SetSourceParameter("Neg", WrapFinalMask(negSurface));
        }

        if (!isAcrylic && paintBrush != null)
        {
            finalBrush.SetSourceParameter("StrokePaint", paintBrush);
        }
    }

    [SupportedOSPlatform("windows10.0.18362")]
    private void ApplyParameters(
        PipelineKey key,
        float surfacePad,
        float brushPad,
        IReadOnlyList<StrokeMorphologyPass> outerPasses,
        IReadOnlyList<StrokeMorphologyPass> innerPasses)
    {
        if (_props == null || _contentSurface == null || _activeOutputVisual == null)
        {
            return;
        }

        if (_activeSurfacePad != surfacePad)
        {
            _activeSurfacePad = surfacePad;
            _props.InsertScalar("pad", surfacePad);
            _contentSurface.SourceOffset = new Vector2(-surfacePad * key.EffectiveScale, -surfacePad * key.EffectiveScale);
            _activeOutputVisual.Offset = new Vector3(-surfacePad, -surfacePad, 0f);
            if (_brushRedirectVisual != null)
            {
                _brushRedirectVisual.Offset = Vector3.Zero;
            }
        }

        if (_brushSurfaceObj != null)
        {
            var diff = MathF.Max(0f, surfacePad - brushPad);
            _brushSurfaceObj.SourceOffset = new Vector2(-diff * key.EffectiveScale, -diff * key.EffectiveScale);
        }

        ApplyStagePasses(_outerStages, outerPasses, isErode: false);
        ApplyStagePasses(_innerStages, innerPasses, isErode: true);
    }

    private static void ApplyStagePasses(
        List<CompositionEffectBrush> stages,
        IReadOnlyList<StrokeMorphologyPass> passes,
        bool isErode)
    {
        for (var i = 0; i < stages.Count; i++)
        {
            var pass = passes[i];
            stages[i].Properties.InsertScalar("Blur.BlurAmount", pass.BlurAmount);
            stages[i].Properties.InsertScalar("Threshold.Source1Amount", pass.Gain);
            stages[i].Properties.InsertScalar("Threshold.Offset", isErode ? pass.ErodeOffset : pass.DilateOffset);
        }
    }

    private void ObserveSolidBrush(SolidColorBrush? brush)
    {
        if (ReferenceEquals(_observedSolidBrush, brush))
        {
            return;
        }

        ReleaseObservedSolidBrush();
        if (brush == null)
        {
            return;
        }

        _observedSolidBrush = brush;
        _solidColorCallbackToken = brush.RegisterPropertyChangedCallback(
            SolidColorBrush.ColorProperty,
            (_, _) => UpdateSolidStrokeColor());
        _solidOpacityCallbackToken = brush.RegisterPropertyChangedCallback(
            Brush.OpacityProperty,
            (_, _) => UpdateSolidStrokeColor());
    }

    private void ReleaseObservedSolidBrush()
    {
        if (_observedSolidBrush == null)
        {
            return;
        }

        var brush = _observedSolidBrush;
        var colorToken = _solidColorCallbackToken;
        var opacityToken = _solidOpacityCallbackToken;
        _observedSolidBrush = null;
        _solidColorCallbackToken = 0;
        _solidOpacityCallbackToken = 0;

        try
        {
            brush.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, colorToken);
            brush.UnregisterPropertyChangedCallback(Brush.OpacityProperty, opacityToken);
        }
        catch
        {
        }
    }

    private void UpdateSolidStrokeColor()
    {
        if (_solidStrokeBrush == null || _observedSolidBrush == null)
        {
            return;
        }

        _solidStrokeBrush.Color = ComputeEffectiveColor(_observedSolidBrush);
    }

    private static Color ComputeEffectiveColor(SolidColorBrush brush)
    {
        var color = brush.Color;
        var opacity = double.IsFinite(brush.Opacity) ? Math.Clamp(brush.Opacity, 0.0, 1.0) : 1.0;
        var alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private void UpdateBrushSurfaceGeometry(FrameworkElement source, Border brushSurface, float brushPad)
    {
        _activeBrushPad = brushPad;
        var width = source.ActualWidth;
        var height = source.ActualHeight;
        if (width > 0 && height > 0)
        {
            ApplyBrushLayout(brushSurface, width + brushPad * 2, height + brushPad * 2, -brushPad, -brushPad);
        }
        else
        {
            ApplyBrushLayout(brushSurface, 0, 0, 0, 0);
        }
    }

    private void ClearBrushSurface(Border brushSurface)
    {
        _activeBrushPad = 0f;
        if (brushSurface.Background != null)
        {
            brushSurface.Background = null;
        }

        ApplyBrushLayout(brushSurface, 0, 0, 0, 0);
    }

    private void ApplyBrushLayout(Border brushSurface, double width, double height, double left, double top)
    {
        if (_appliedBrushWidth != width)
        {
            _appliedBrushWidth = width;
            brushSurface.Width = width;
        }

        if (_appliedBrushHeight != height)
        {
            _appliedBrushHeight = height;
            brushSurface.Height = height;
        }

        if (_appliedBrushLeft != left)
        {
            _appliedBrushLeft = left;
            Canvas.SetLeft(brushSurface, left);
        }

        if (_appliedBrushTop != top)
        {
            _appliedBrushTop = top;
            Canvas.SetTop(brushSurface, top);
        }
    }

    private void ObserveSourceSize(FrameworkElement source)
    {
        if (ReferenceEquals(_observedSource, source))
        {
            return;
        }

        ReleaseObservedSource();
        _observedSource = source;
        source.SizeChanged += OnSourceSizeChanged;
    }

    private void ReleaseObservedSource()
    {
        if (_observedSource != null)
        {
            _observedSource.SizeChanged -= OnSourceSizeChanged;
            _observedSource = null;
        }
    }

    private void OnSourceSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_brushSurface is { } brushSurface
            && _observedSource is { } source
            && _activeKey is { BrushMode: not BrushMode.SolidColor })
        {
            UpdateBrushSurfaceGeometry(source, brushSurface, _activeBrushPad);
        }
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

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => SyncStroke();

    private double GetRasterizationScale()
    {
        if (!IsXamlRootSupported)
        {
            return 1.0;
        }

        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        return double.IsFinite(scale) && scale > 0 ? scale : 1.0;
    }

    private void UpdateOriginalVisibility(FrameworkElement original, bool hideOriginal)
    {
        if (hideOriginal)
        {
            var visual = ElementCompositionPreview.GetElementVisual(original);
            if (!ReferenceEquals(_hiddenOriginalVisual, visual))
            {
                RestoreOriginalVisibility();
                _hiddenOriginalVisual = visual;
            }

            visual.Opacity = 0f;
        }
        else
        {
            RestoreOriginalVisibility();
        }
    }

    private void RestoreOriginalVisibility()
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

    private void RestoreBrushContainerVisibility()
    {
        if (_hiddenBrushContainerVisual == null)
        {
            return;
        }

        try
        {
            _hiddenBrushContainerVisual.Opacity = 1f;
        }
        catch
        {
        }

        _hiddenBrushContainerVisual = null;
    }

    private void OnRedirectSourceChanged(object? sender, EventArgs args) => SyncStroke();

    private FrameworkElement? ResolveEffectiveSource()
    {
        ReleaseObservedProviders();
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

    private void ReleaseStroke()
    {
        ReleaseObservedProviders();
        ReleaseObservedSource();
        ReleaseObservedSolidBrush();
        ReleaseStrokeVisualsOnly();
        RestoreOriginalVisibility();
        if (_brushSurface != null)
        {
            ClearBrushSurface(_brushSurface);
        }

        SetVisualOutset(0);
    }

    private void ReleaseStrokeVisualsOnly()
    {
        var host = _activeKey?.Host;
        var resources = _resources;
        _activeKey = null;
        _resources = null;
        _outerStages.Clear();
        _innerStages.Clear();
        _props = null;
        _contentSurface = null;
        _brushSurfaceObj = null;
        _solidStrokeBrush = null;
        _activeOutputVisual = null;
        _brushRedirectVisual = null;
        _activeSurfacePad = float.NaN;

        RestoreBrushContainerVisibility();

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

    private void SetVisualOutset(double value)
    {
        if (_visualOutset == value)
        {
            return;
        }

        _visualOutset = value;
        VisualOutsetChanged?.Invoke(this, EventArgs.Empty);
    }

    private static T TrackEffect<T>(T effect, CompositeDisposable graphNodes) where T : class, IDisposable
    {
        graphNodes.Add(effect);
        return effect;
    }

    private enum BrushMode
    {
        SolidColor,
        VisualSurface,
        Acrylic
    }

    private sealed record PipelineKey(
        FrameworkElement Host,
        FrameworkElement Source,
        BrushMode BrushMode,
        StrokePosition Position,
        bool IsHollow,
        int OuterPassCount,
        bool OuterHalfRes,
        int InnerPassCount,
        bool InnerHalfRes,
        float EffectiveScale);

    /// <summary>按 Compositor 缓存已编译的效果工厂，跨粗细档位切换时免于重复编译 D2D 着色器。</summary>
    private sealed class EffectFactoryCache
    {
        private readonly Compositor _compositor;
        private readonly Lazy<CompositionEffectFactory> _alphaExtract;
        private readonly Lazy<CompositionEffectFactory> _blurThreshold;
        private readonly Lazy<CompositionEffectFactory> _downscaleBlurThreshold;
        private readonly Lazy<CompositionEffectFactory> _upscaleBlurThreshold;
        private readonly Lazy<CompositionEffectFactory> _finalOutsideSolid;
        private readonly Lazy<CompositionEffectFactory> _finalInsideSolid;
        private readonly Lazy<CompositionEffectFactory> _finalDifference;

        public EffectFactoryCache(Compositor compositor)
        {
            _compositor = compositor;
            _alphaExtract = new Lazy<CompositionEffectFactory>(CreateAlphaExtractFactory);
            _blurThreshold = new Lazy<CompositionEffectFactory>(() => CreateBlurThresholdFactory(1f));
            _downscaleBlurThreshold = new Lazy<CompositionEffectFactory>(() => CreateBlurThresholdFactory(0.5f));
            _upscaleBlurThreshold = new Lazy<CompositionEffectFactory>(() => CreateBlurThresholdFactory(2f));
            _finalOutsideSolid = new Lazy<CompositionEffectFactory>(CreateFinalOutsideSolidFactory);
            _finalInsideSolid = new Lazy<CompositionEffectFactory>(CreateFinalInsideSolidFactory);
            _finalDifference = new Lazy<CompositionEffectFactory>(CreateFinalDifferenceFactory);
        }

        public CompositionEffectFactory AlphaExtract => _alphaExtract.Value;

        public CompositionEffectFactory BlurThreshold => _blurThreshold.Value;

        public CompositionEffectFactory DownscaleBlurThreshold => _downscaleBlurThreshold.Value;

        public CompositionEffectFactory UpscaleBlurThreshold => _upscaleBlurThreshold.Value;

        public CompositionEffectFactory GetFinalFactory(StrokePosition position, bool isHollow, bool isAcrylicBrush)
        {
            if (position == StrokePosition.Outside && !isHollow)
            {
                return _finalOutsideSolid.Value;
            }

            if (position == StrokePosition.Inside && !isHollow && !isAcrylicBrush)
            {
                return _finalInsideSolid.Value;
            }

            return _finalDifference.Value;
        }

        private CompositionEffectFactory CreateAlphaExtractFactory()
        {
            using var graph = new ColorMatrixEffect
            {
                Source = new CompositionEffectSourceParameter("Content"),
                AlphaMode = CanvasAlphaMode.Premultiplied,
                ClampOutput = true,
                ColorMatrix = PremultipliedAlphaToWhiteMatrix
            };
            return _compositor.CreateEffectFactory(graph);
        }

        private CompositionEffectFactory CreateBlurThresholdFactory(float inputScale)
        {
            using var nodes = new CompositeDisposable();
            IGraphicsEffectSource blurInput = new CompositionEffectSourceParameter("S0");
            if (MathF.Abs(inputScale - 1f) > 1e-4f)
            {
                // Windows.UI.Composition 要求 Transform2DEffect 直接以效果源参数作为输入。
                blurInput = TrackEffect(new Transform2DEffect
                {
                    Source = blurInput,
                    InterpolationMode = CanvasImageInterpolation.Linear,
                    BorderMode = EffectBorderMode.Soft,
                    TransformMatrix = Matrix3x2.CreateScale(inputScale)
                }, nodes);
            }

            var blur = TrackEffect(new GaussianBlurEffect
            {
                Name = "Blur",
                Source = blurInput,
                BlurAmount = 1f,
                BorderMode = EffectBorderMode.Soft,
                Optimization = EffectOptimization.Quality
            }, nodes);

            var zeroSource = TrackEffect(new ColorSourceEffect { Color = Colors.Transparent }, nodes);
            var threshold = TrackEffect(new ArithmeticCompositeEffect
            {
                Name = "Threshold",
                Source1 = blur,
                Source2 = zeroSource,
                Source1Amount = 1f,
                Source2Amount = 0f,
                MultiplyAmount = 0f,
                Offset = 0f,
                ClampOutput = true
            }, nodes);

            return _compositor.CreateEffectFactory(
                threshold,
                (string[])["Blur.BlurAmount", "Threshold.Source1Amount", "Threshold.Offset"]);
        }

        private CompositionEffectFactory CreateFinalOutsideSolidFactory()
        {
            using var nodes = new CompositeDisposable();
            var boostPm = TrackEffect(new ArithmeticCompositeEffect
            {
                Source1 = new CompositionEffectSourceParameter("D0"),
                Source2 = new CompositionEffectSourceParameter("A0"),
                Source1Amount = 2f,
                Source2Amount = -2f,
                MultiplyAmount = 0f,
                Offset = 0f,
                ClampOutput = true
            }, nodes);
            var boostOpaque = TrackEffect(new ColorMatrixEffect
            {
                Source = boostPm,
                AlphaMode = CanvasAlphaMode.Premultiplied,
                ClampOutput = true,
                ColorMatrix = AlphaToOpaqueGrayMatrix
            }, nodes);
            var dOpaque = TrackEffect(new ColorMatrixEffect
            {
                Source = new CompositionEffectSourceParameter("D1"),
                AlphaMode = CanvasAlphaMode.Premultiplied,
                ClampOutput = true,
                ColorMatrix = AlphaToOpaqueGrayMatrix
            }, nodes);
            var clampedOpaque = TrackEffect(new BlendEffect
            {
                Mode = BlendEffectMode.Darken,
                Background = dOpaque,
                Foreground = boostOpaque
            }, nodes);
            var mask = TrackEffect(new ColorMatrixEffect
            {
                Source = clampedOpaque,
                AlphaMode = CanvasAlphaMode.Premultiplied,
                ClampOutput = true,
                ColorMatrix = OpaqueGrayToAlphaMaskMatrix
            }, nodes);
            var root = TrackEffect(new AlphaMaskEffect
            {
                Source = new CompositionEffectSourceParameter("StrokePaint"),
                AlphaMask = mask
            }, nodes);
            return _compositor.CreateEffectFactory(root);
        }

        private CompositionEffectFactory CreateFinalInsideSolidFactory()
        {
            using var nodes = new CompositeDisposable();
            var whiteSource = TrackEffect(new ColorSourceEffect { Color = Colors.White }, nodes);
            var bandMask = TrackEffect(new ArithmeticCompositeEffect
            {
                Source1 = whiteSource,
                Source2 = new CompositionEffectSourceParameter("E0"),
                Source1Amount = 1f,
                Source2Amount = -1f,
                MultiplyAmount = 0f,
                Offset = 0f,
                ClampOutput = true
            }, nodes);
            var strokeBand = TrackEffect(new AlphaMaskEffect
            {
                Source = new CompositionEffectSourceParameter("StrokePaint"),
                AlphaMask = bandMask
            }, nodes);
            var root = TrackEffect(new CompositeEffect
            {
                Mode = CanvasComposite.SourceAtop,
                Sources = { new CompositionEffectSourceParameter("Content"), strokeBand }
            }, nodes);
            return _compositor.CreateEffectFactory(root);
        }

        private CompositionEffectFactory CreateFinalDifferenceFactory()
        {
            using var nodes = new CompositeDisposable();
            var differenceMask = TrackEffect(new ArithmeticCompositeEffect
            {
                Source1 = new CompositionEffectSourceParameter("Pos"),
                Source2 = new CompositionEffectSourceParameter("Neg"),
                Source1Amount = 1f,
                Source2Amount = -1f,
                MultiplyAmount = 0f,
                Offset = 0f,
                ClampOutput = true
            }, nodes);
            var root = TrackEffect(new AlphaMaskEffect
            {
                Source = new CompositionEffectSourceParameter("StrokePaint"),
                AlphaMask = differenceMask
            }, nodes);
            return _compositor.CreateEffectFactory(root);
        }
    }
}
