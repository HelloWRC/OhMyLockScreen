using System.Numerics;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ScenePresenter : ISceneParallaxHost
{
    private const string SceneViewportSizeProperty = "ViewportSize";
    private const string SceneMultiplierProperty = "Multiplier";
    private const string MotionOffsetProperty = "Offset";
    private const string MotionScaleProperty = "Scale";
    private const string MotionMaxAmountProperty = "MaxAmount";
    private const string MotionClampedOffsetProperty = "ClampedOffset";
    private const string MotionEffectiveScaleProperty = "EffectiveScale";
    private const string VisualTranslationProperty = "Translation";
    private const string VisualScaleProperty = "Scale";
    private const string VisualCenterPointProperty = "CenterPoint";

    public static readonly DependencyProperty ParallaxInputSurfaceProperty = DependencyProperty.Register(
        nameof(ParallaxInputSurface), typeof(UIElement), typeof(ScenePresenter),
        new PropertyMetadata(null, OnParallaxInputSurfaceChanged));

    /// <summary>可选的路由鼠标事件来源，供场景编辑器跟踪页面内的鼠标位置。</summary>
    public UIElement? ParallaxInputSurface
    {
        get => (UIElement?)GetValue(ParallaxInputSurfaceProperty);
        set => SetValue(ParallaxInputSurfaceProperty, value);
    }

    /// <summary>供子图层接入的共享视差宿主。</summary>
    public ISceneParallaxHost ParallaxHost => this;

    internal bool SuppressParallaxWithoutBackground { get; set; }

    internal event EventHandler? LayerParallaxUpdated;

    internal bool HasActiveLayerParallaxAnimation
    {
        get
        {
            if (!IsActive || _layerParallaxes.Count == 0)
            {
                return false;
            }

            var now = _parallaxClock.GetTimestamp();
            if (_isUnlockAnimatingMultiplier && IsMultiplierUnlockAnimatingAt(now))
            {
                return true;
            }

            foreach (var entry in _layerParallaxes)
            {
                if (entry.Motion.IsAnimatingAt(now))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private ParallaxMotion _backgroundParallax = null!;
    private readonly List<LayerParallaxRegistration> _layerParallaxes = [];
    private UIElement? _parallaxInput;
    private Point? _parallaxPointer;
    private double _parallaxMultiplier = 1;
    private TimeProvider _parallaxClock = TimeProvider.System;
    private bool _isParallaxFrozen;
    private bool _isUnlockAnimatingMultiplier;
    private double _multiplierUnlockStart = 1;
    private long _multiplierUnlockStartTimestamp;

    private Compositor? _parallaxCompositor;
    private CompositionPropertySet? _sceneParallaxProps;
    private CubicBezierEasingFunction? _parallaxOutQuintEasing;
    private CubicBezierEasingFunction? _parallaxUnlockEasing;
    private ExpressionAnimation? _parallaxCenterPointExpression;
    private ExpressionAnimation? _parallaxFreezeExpression;

    private bool CanAnimateParallax =>
        IsActive && !_backgroundSuspended && _parallaxMultiplier == 1 && _unlockStoryboard == null;

    private void InitializeParallax()
    {
        _backgroundParallax = new ParallaxMotion(BackgroundParallaxHost, _parallaxClock);
    }

    private static void OnParallaxInputSurfaceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (ScenePresenter)sender;
        presenter.UpdateParallaxInput();
        presenter.UpdateParallaxTarget();
    }

    ISceneParallaxRegistration ISceneParallaxHost.Register(UIElement target, double? strengthOverride)
    {
        ArgumentNullException.ThrowIfNull(target);

        var registration = new LayerParallaxRegistration(
            this,
            new ParallaxMotion(target, _parallaxClock),
            ParallaxMotionMath.NormalizeStrengthOverride(strengthOverride));
        _layerParallaxes.Add(registration);

        var now = _parallaxClock.GetTimestamp();
        if (IsActive && EnsureParallaxComposition())
        {
            registration.Motion.Attach(
                _parallaxCompositor!,
                _sceneParallaxProps!,
                _parallaxCenterPointExpression!);

            var width = BackgroundViewport.ActualWidth;
            var height = BackgroundViewport.ActualHeight;
            var canAnimate = CanAnimateParallax && !_isParallaxFrozen;
            var changed = registration.Motion.SetStrength(
                GetLayerParallaxStrength(registration.StrengthOverride),
                now,
                canAnimate,
                _parallaxOutQuintEasing);
            changed |= registration.Motion.UpdateTarget(
                GetPointerInScene(width, height),
                width,
                height,
                now,
                canAnimate,
                _parallaxOutQuintEasing);
            if (changed)
            {
                RaiseLayerParallaxUpdated();
            }
        }

        return registration;
    }

    internal void SampleParallaxPointer(PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse || _parallaxInput == null)
        {
            return;
        }

        SampleParallaxPoint(args.GetCurrentPoint(_parallaxInput).Position);
    }

    internal void SampleParallaxPoint(Point position)
    {
        if (_parallaxInput == null)
        {
            return;
        }

        _parallaxPointer = position;
        UpdateParallaxTarget();
    }

    internal bool TryGetLayerParallaxMatrix(UIElement target, out BrushEditorMatrix matrix)
    {
        matrix = BrushEditorMatrix.Identity;
        if (!IsActive)
        {
            return false;
        }

        foreach (var entry in _layerParallaxes)
        {
            if (!ReferenceEquals(entry.Motion.Target, target))
            {
                continue;
            }

            var now = _parallaxClock.GetTimestamp();
            var viewportWidth = BackgroundViewport.ActualWidth;
            var viewportHeight = BackgroundViewport.ActualHeight;
            var targetWidth = target is FrameworkElement fe && fe.ActualWidth > 0 ? fe.ActualWidth : viewportWidth;
            var targetHeight = target is FrameworkElement fe2 && fe2.ActualHeight > 0 ? fe2.ActualHeight : viewportHeight;
            var multiplier = EvaluateParallaxMultiplier(now);
            matrix = entry.Motion.EvaluateMatrix(
                now,
                viewportWidth,
                viewportHeight,
                targetWidth,
                targetHeight,
                multiplier);
            return true;
        }

        return false;
    }

    private void AttachParallaxComposition()
    {
        _backgroundParallax.Clock = _parallaxClock;
        foreach (var entry in _layerParallaxes)
        {
            entry.Motion.Clock = _parallaxClock;
        }

        if (!EnsureParallaxComposition())
        {
            return;
        }

        _backgroundParallax.Attach(_parallaxCompositor!, _sceneParallaxProps!, _parallaxCenterPointExpression!);
        foreach (var entry in _layerParallaxes)
        {
            entry.Motion.Attach(_parallaxCompositor!, _sceneParallaxProps!, _parallaxCenterPointExpression!);
        }

        SyncSceneViewportSize();
        SyncSceneMultiplier();
    }

    private void DetachParallaxComposition()
    {
        UpdateParallaxInput();
        _isParallaxFrozen = false;
        _isUnlockAnimatingMultiplier = false;

        _backgroundParallax.Detach(resetVisual: true);
        var layerChanged = false;
        foreach (var entry in _layerParallaxes)
        {
            layerChanged |= entry.Motion.Detach(resetVisual: true);
        }

        ReleaseSceneParallaxResources();
        if (layerChanged)
        {
            RaiseLayerParallaxUpdated();
        }
    }

    private bool EnsureParallaxComposition()
    {
        var visual = ElementCompositionPreview.GetElementVisual(BackgroundParallaxHost);
        var compositor = visual.Compositor;
        if (ReferenceEquals(_parallaxCompositor, compositor)
            && _sceneParallaxProps != null
            && _parallaxOutQuintEasing != null
            && _parallaxUnlockEasing != null
            && _parallaxCenterPointExpression != null
            && _parallaxFreezeExpression != null)
        {
            return true;
        }

        _backgroundParallax.Detach(resetVisual: true);
        foreach (var entry in _layerParallaxes)
        {
            entry.Motion.Detach(resetVisual: true);
        }

        ReleaseSceneParallaxResources();

        _parallaxCompositor = compositor;
        _sceneParallaxProps = compositor.CreatePropertySet();
        _sceneParallaxProps.InsertVector2(
            SceneViewportSizeProperty,
            new Vector2((float)Math.Max(0, BackgroundViewport.ActualWidth), (float)Math.Max(0, BackgroundViewport.ActualHeight)));
        _sceneParallaxProps.InsertScalar(SceneMultiplierProperty, (float)_parallaxMultiplier);

        _parallaxOutQuintEasing = compositor.CreateCubicBezierEasingFunction(
            ParallaxMotionMath.OutQuintControlPoint1,
            ParallaxMotionMath.OutQuintControlPoint2);
        _parallaxUnlockEasing = compositor.CreateCubicBezierEasingFunction(
            ParallaxMotionMath.UnlockControlPoint1,
            ParallaxMotionMath.UnlockControlPoint2);
        _parallaxCenterPointExpression = compositor.CreateExpressionAnimation(
            "Vector3(this.Target.Size.X * 0.5, this.Target.Size.Y * 0.5, 0)");
        _parallaxFreezeExpression = compositor.CreateExpressionAnimation("this.StartingValue");
        return true;
    }

    private void ReleaseSceneParallaxResources()
    {
        _parallaxFreezeExpression?.Dispose();
        _parallaxFreezeExpression = null;
        _parallaxCenterPointExpression?.Dispose();
        _parallaxCenterPointExpression = null;
        _parallaxUnlockEasing?.Dispose();
        _parallaxUnlockEasing = null;
        _parallaxOutQuintEasing?.Dispose();
        _parallaxOutQuintEasing = null;
        _sceneParallaxProps?.Dispose();
        _sceneParallaxProps = null;
        _parallaxCompositor = null;
    }

    private void SyncSceneViewportSize()
    {
        if (_sceneParallaxProps == null)
        {
            return;
        }

        var width = Math.Max(0, BackgroundViewport.ActualWidth);
        var height = Math.Max(0, BackgroundViewport.ActualHeight);
        _sceneParallaxProps.InsertVector2(SceneViewportSizeProperty, new Vector2((float)width, (float)height));
    }

    private void SyncSceneMultiplier()
    {
        if (_sceneParallaxProps == null)
        {
            return;
        }

        _sceneParallaxProps.StopAnimation(SceneMultiplierProperty);
        _sceneParallaxProps.InsertScalar(SceneMultiplierProperty, (float)_parallaxMultiplier);
    }

    private double GetBackgroundParallaxStrength()
    {
        var background = Scene?.Background;
        return IsActive && background != null
            && (!SuppressParallaxWithoutBackground || background.Mode != BackgroundMode.None)
            ? background.ParallaxStrength
            : 0;
    }

    private double GetLayerParallaxStrength(double? strengthOverride)
    {
        if (!IsActive)
        {
            return 0;
        }

        return strengthOverride ?? Scene?.Background?.ParallaxStrength ?? 0;
    }

    private void UpdateLayerParallaxStrength(LayerParallaxRegistration registration)
    {
        if (!IsActive)
        {
            return;
        }

        var now = _parallaxClock.GetTimestamp();
        var width = BackgroundViewport.ActualWidth;
        var height = BackgroundViewport.ActualHeight;
        var canAnimate = CanAnimateParallax && !_isParallaxFrozen;
        var changed = registration.Motion.SetStrength(
            GetLayerParallaxStrength(registration.StrengthOverride),
            now,
            canAnimate,
            _parallaxOutQuintEasing);
        changed |= registration.Motion.UpdateTarget(
            GetPointerInScene(width, height),
            width,
            height,
            now,
            canAnimate,
            _parallaxOutQuintEasing);
        if (changed)
        {
            RaiseLayerParallaxUpdated();
        }
    }

    private void UnregisterLayerParallax(LayerParallaxRegistration registration)
    {
        if (!_layerParallaxes.Remove(registration))
        {
            return;
        }

        if (registration.Motion.Detach(resetVisual: true))
        {
            RaiseLayerParallaxUpdated();
        }
    }

    private void RefreshParallaxStrength()
    {
        UpdateParallaxInput();
        if (!IsActive)
        {
            return;
        }

        var now = _parallaxClock.GetTimestamp();
        var width = BackgroundViewport.ActualWidth;
        var height = BackgroundViewport.ActualHeight;
        var canAnimate = CanAnimateParallax;
        if (canAnimate)
        {
            _isParallaxFrozen = false;
        }

        _backgroundParallax.SetStrength(
            GetBackgroundParallaxStrength(),
            now,
            canAnimate,
            _parallaxOutQuintEasing);

        var layerChanged = false;
        foreach (var entry in _layerParallaxes)
        {
            layerChanged |= entry.Motion.SetStrength(
                GetLayerParallaxStrength(entry.StrengthOverride),
                now,
                canAnimate,
                _parallaxOutQuintEasing);
        }

        var pointerInScene = GetPointerInScene(width, height);
        _backgroundParallax.UpdateTarget(
            pointerInScene,
            width,
            height,
            now,
            canAnimate,
            _parallaxOutQuintEasing);
        foreach (var entry in _layerParallaxes)
        {
            layerChanged |= entry.Motion.UpdateTarget(
                pointerInScene,
                width,
                height,
                now,
                canAnimate,
                _parallaxOutQuintEasing);
        }

        if (layerChanged)
        {
            RaiseLayerParallaxUpdated();
        }
    }

    private void UpdateParallaxInput()
    {
        var input = IsActive ? ParallaxInputSurface ?? SceneRoot : null;
        if (ReferenceEquals(input, _parallaxInput))
        {
            return;
        }

        if (_parallaxInput != null)
        {
            _parallaxInput.PointerEntered -= OnParallaxPointerChanged;
            _parallaxInput.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnParallaxPointerChanged));
        }

        _parallaxPointer = null;
        if (IsActive)
        {
            ResetParallaxMotion();
        }

        _parallaxInput = input;
        if (input != null)
        {
            input.PointerEntered += OnParallaxPointerChanged;
            input.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnParallaxPointerChanged), true);
        }
    }

    private void OnParallaxPointerChanged(object sender, PointerRoutedEventArgs args)
    {
        SampleParallaxPointer(args);
    }

    private void UpdateParallaxViewport()
    {
        SyncSceneViewportSize();
        if (!IsActive)
        {
            return;
        }

        var now = _parallaxClock.GetTimestamp();
        var width = BackgroundViewport.ActualWidth;
        var height = BackgroundViewport.ActualHeight;
        var canAnimate = CanAnimateParallax;
        if (canAnimate && _isParallaxFrozen)
        {
            _isParallaxFrozen = false;
            ResumeFrozenParallax(now);
        }

        var pointerInScene = GetPointerInScene(width, height);
        _backgroundParallax.UpdateTarget(
            pointerInScene,
            width,
            height,
            now,
            canAnimate && !_isParallaxFrozen,
            _parallaxOutQuintEasing,
            clampStartToViewport: true);

        var layerChanged = false;
        foreach (var entry in _layerParallaxes)
        {
            layerChanged |= entry.Motion.UpdateTarget(
                pointerInScene,
                width,
                height,
                now,
                canAnimate && !_isParallaxFrozen,
                _parallaxOutQuintEasing,
                clampStartToViewport: true);
        }

        if (layerChanged)
        {
            RaiseLayerParallaxUpdated();
        }
    }

    private Point? GetPointerInScene(double width, double height)
    {
        if (width <= 0 || height <= 0 || _parallaxInput == null || _parallaxPointer is not { } pointer)
        {
            return null;
        }

        return ReferenceEquals(_parallaxInput, SceneRoot)
            ? pointer
            : _parallaxInput.TransformToVisual(SceneRoot).TransformPoint(pointer);
    }

    private void UpdateParallaxTarget()
    {
        if (!IsActive)
        {
            return;
        }

        var now = _parallaxClock.GetTimestamp();
        var width = BackgroundViewport.ActualWidth;
        var height = BackgroundViewport.ActualHeight;
        var canAnimate = CanAnimateParallax;
        var wasFrozen = _isParallaxFrozen;
        if (canAnimate && wasFrozen)
        {
            _isParallaxFrozen = false;
            ResumeFrozenParallax(now);
        }

        var pointerInScene = GetPointerInScene(width, height);
        _backgroundParallax.UpdateTarget(
            pointerInScene,
            width,
            height,
            now,
            canAnimate && !_isParallaxFrozen,
            _parallaxOutQuintEasing);

        var layerChanged = false;
        foreach (var entry in _layerParallaxes)
        {
            layerChanged |= entry.Motion.UpdateTarget(
                pointerInScene,
                width,
                height,
                now,
                canAnimate && !_isParallaxFrozen,
                _parallaxOutQuintEasing);
        }

        if (layerChanged || (wasFrozen && canAnimate && _layerParallaxes.Count > 0))
        {
            RaiseLayerParallaxUpdated();
        }
    }

    private void ResumeFrozenParallax(long now)
    {
        _backgroundParallax.ResumeScale(
            GetBackgroundParallaxStrength(),
            now,
            _parallaxOutQuintEasing);
        foreach (var entry in _layerParallaxes)
        {
            entry.Motion.ResumeScale(
                GetLayerParallaxStrength(entry.StrengthOverride),
                now,
                _parallaxOutQuintEasing);
        }
    }

    private void FreezeParallaxMotion()
    {
        if (_isParallaxFrozen)
        {
            return;
        }

        _isParallaxFrozen = true;
        var now = _parallaxClock.GetTimestamp();
        _backgroundParallax.Freeze(now, _parallaxFreezeExpression);
        foreach (var entry in _layerParallaxes)
        {
            entry.Motion.Freeze(now, _parallaxFreezeExpression);
        }
    }

    private void SetParallaxMultiplier(double multiplier)
    {
        if (_parallaxMultiplier == multiplier && !_isUnlockAnimatingMultiplier)
        {
            return;
        }

        _parallaxMultiplier = multiplier;
        _isUnlockAnimatingMultiplier = false;
        if (multiplier < 1)
        {
            FreezeParallaxMotion();
        }

        SyncSceneMultiplier();

        if (multiplier == 1 && CanAnimateParallax)
        {
            UpdateParallaxTarget();
        }
        else if (_layerParallaxes.Count > 0)
        {
            RaiseLayerParallaxUpdated();
        }
    }

    private void StartParallaxUnlockAnimation()
    {
        _isUnlockAnimatingMultiplier = true;
        _multiplierUnlockStart = _parallaxMultiplier;
        _multiplierUnlockStartTimestamp = _parallaxClock.GetTimestamp();

        if (_sceneParallaxProps != null && _parallaxCompositor != null && _parallaxUnlockEasing != null)
        {
            using var animation = _parallaxCompositor.CreateScalarKeyFrameAnimation();
            animation.Duration = UnlockDuration;
            animation.InsertKeyFrame(0.0f, (float)_parallaxMultiplier);
            animation.InsertKeyFrame(1.0f, 0.0f, _parallaxUnlockEasing);
            _sceneParallaxProps.StartAnimation(SceneMultiplierProperty, animation);
        }

        if (_layerParallaxes.Count > 0)
        {
            RaiseLayerParallaxUpdated();
        }
    }

    private void StopParallaxUnlockAnimation()
    {
        if (!_isUnlockAnimatingMultiplier)
        {
            return;
        }

        _isUnlockAnimatingMultiplier = false;
        SyncSceneMultiplier();
    }

    private void ResetParallaxMotion()
    {
        var now = _parallaxClock.GetTimestamp();
        _isParallaxFrozen = true;
        _backgroundParallax.ResetMotion(now, _parallaxFreezeExpression);

        var layerChanged = false;
        foreach (var entry in _layerParallaxes)
        {
            layerChanged |= entry.Motion.ResetMotion(now, _parallaxFreezeExpression);
        }

        if (layerChanged)
        {
            RaiseLayerParallaxUpdated();
        }
    }

    private bool IsMultiplierUnlockAnimatingAt(long now)
    {
        var elapsed = _parallaxClock.GetElapsedTime(_multiplierUnlockStartTimestamp, now);
        return elapsed < UnlockDuration;
    }

    private double EvaluateParallaxMultiplier(long now)
    {
        if (!_isUnlockAnimatingMultiplier)
        {
            return _parallaxMultiplier;
        }

        var elapsed = _parallaxClock.GetElapsedTime(_multiplierUnlockStartTimestamp, now);
        if (elapsed <= TimeSpan.Zero)
        {
            return _multiplierUnlockStart;
        }

        if (elapsed >= UnlockDuration)
        {
            return 0;
        }

        var progress = elapsed.TotalMilliseconds / UnlockDuration.TotalMilliseconds;
        var eased = ParallaxMotionMath.EvaluateCubicBezier(
            progress,
            ParallaxMotionMath.UnlockControlPoint1,
            ParallaxMotionMath.UnlockControlPoint2);
        return _multiplierUnlockStart * (1 - eased);
    }

    private void RaiseLayerParallaxUpdated()
    {
        LayerParallaxUpdated?.Invoke(this, EventArgs.Empty);
    }

    private sealed class LayerParallaxRegistration(
        ScenePresenter owner,
        ParallaxMotion motion,
        double? strengthOverride) : ISceneParallaxRegistration
    {
        private bool _disposed;

        public ParallaxMotion Motion { get; } = motion;

        public double? StrengthOverride { get; private set; } = strengthOverride;

        public void SetStrengthOverride(double? strengthOverride)
        {
            if (_disposed)
            {
                return;
            }

            var normalized = ParallaxMotionMath.NormalizeStrengthOverride(strengthOverride);
            if (StrengthOverride == normalized)
            {
                return;
            }

            StrengthOverride = normalized;
            owner.UpdateLayerParallaxStrength(this);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.UnregisterLayerParallax(this);
        }
    }

    private sealed class ParallaxMotion(UIElement target, TimeProvider clock)
    {
        private const string ClampedOffsetExpressionText =
            "scene.ViewportSize.X > 0 && scene.ViewportSize.Y > 0 " +
            "? Vector2(" +
                "Clamp(motion.Offset.X, -scene.ViewportSize.X * Max(motion.Scale - 1, motion.MaxAmount) * 0.5, scene.ViewportSize.X * Max(motion.Scale - 1, motion.MaxAmount) * 0.5), " +
                "Clamp(motion.Offset.Y, -scene.ViewportSize.Y * Max(motion.Scale - 1, motion.MaxAmount) * 0.5, scene.ViewportSize.Y * Max(motion.Scale - 1, motion.MaxAmount) * 0.5)) " +
            ": Vector2(0, 0)";

        private const string EffectiveScaleExpressionText =
            "1 + ((scene.ViewportSize.X > 0 && scene.ViewportSize.Y > 0 " +
            "? Max(motion.Scale, 1 + 2 * Max(Abs(motion.ClampedOffset.X) / Max(scene.ViewportSize.X, 0.0001), Abs(motion.ClampedOffset.Y) / Max(scene.ViewportSize.Y, 0.0001))) " +
            ": motion.Scale) - 1) * scene.Multiplier";

        private const string TranslationExpressionText =
            "Vector3(motion.ClampedOffset.X * scene.Multiplier, motion.ClampedOffset.Y * scene.Multiplier, 0)";

        private const string ScaleExpressionText =
            "Vector3(motion.EffectiveScale, motion.EffectiveScale, 1)";

        private Compositor? _compositor;
        private CompositionPropertySet? _motionProps;
        private ExpressionAnimation? _clampedOffsetExpr;
        private ExpressionAnimation? _effectiveScaleExpr;
        private ExpressionAnimation? _translationExpr;
        private ExpressionAnimation? _scaleExpr;
        private bool _attached;
        private bool _offsetFrozen;
        private bool _scaleFrozen;

        private Vector2 _offsetStart;
        private Vector2 _offsetTarget;
        private long _offsetStartTimestamp;
        private bool _offsetAnimating;

        private double _scaleStart = 1;
        private double _scaleTarget = 1;
        private long _scaleStartTimestamp;
        private bool _scaleAnimating;

        public UIElement Target { get; } = target;

        public TimeProvider Clock { get; set; } = clock;

        public double Amount { get; private set; }

        public Vector2 TargetOffset { get; private set; }

        public bool IsAnimatingAt(long now)
        {
            if (!_offsetFrozen
                && _offsetAnimating
                && Clock.GetElapsedTime(_offsetStartTimestamp, now) < ParallaxMotionMath.PositionDuration)
            {
                return true;
            }

            if (!_scaleFrozen
                && _scaleAnimating
                && Clock.GetElapsedTime(_scaleStartTimestamp, now) < ParallaxMotionMath.ScaleDuration)
            {
                return true;
            }

            return false;
        }

        public void Attach(
            Compositor compositor,
            CompositionPropertySet sceneProps,
            ExpressionAnimation centerPointExpr)
        {
            if (_attached && ReferenceEquals(_compositor, compositor))
            {
                return;
            }

            Detach(resetVisual: true);

            ElementCompositionPreview.SetIsTranslationEnabled(Target, true);
            var visual = ElementCompositionPreview.GetElementVisual(Target);
            if (!ReferenceEquals(visual.Compositor, compositor))
            {
                return;
            }

            visual.Properties.InsertVector3(VisualTranslationProperty, Vector3.Zero);

            var motionProps = compositor.CreatePropertySet();
            motionProps.InsertVector2(MotionOffsetProperty, Vector2.Zero);
            motionProps.InsertScalar(MotionScaleProperty, 1.0f);
            motionProps.InsertScalar(MotionMaxAmountProperty, 0.0f);
            motionProps.InsertVector2(MotionClampedOffsetProperty, Vector2.Zero);
            motionProps.InsertScalar(MotionEffectiveScaleProperty, 1.0f);

            var clampedOffsetExpr = compositor.CreateExpressionAnimation(ClampedOffsetExpressionText);
            clampedOffsetExpr.SetReferenceParameter("scene", sceneProps);
            clampedOffsetExpr.SetReferenceParameter("motion", motionProps);

            var effectiveScaleExpr = compositor.CreateExpressionAnimation(EffectiveScaleExpressionText);
            effectiveScaleExpr.SetReferenceParameter("scene", sceneProps);
            effectiveScaleExpr.SetReferenceParameter("motion", motionProps);

            var translationExpr = compositor.CreateExpressionAnimation(TranslationExpressionText);
            translationExpr.SetReferenceParameter("scene", sceneProps);
            translationExpr.SetReferenceParameter("motion", motionProps);

            var scaleExpr = compositor.CreateExpressionAnimation(ScaleExpressionText);
            scaleExpr.SetReferenceParameter("motion", motionProps);

            motionProps.StartAnimation(MotionClampedOffsetProperty, clampedOffsetExpr);
            motionProps.StartAnimation(MotionEffectiveScaleProperty, effectiveScaleExpr);
            visual.StartAnimation(VisualTranslationProperty, translationExpr);
            visual.StartAnimation(VisualScaleProperty, scaleExpr);
            visual.StartAnimation(VisualCenterPointProperty, centerPointExpr);

            _compositor = compositor;
            _motionProps = motionProps;
            _clampedOffsetExpr = clampedOffsetExpr;
            _effectiveScaleExpr = effectiveScaleExpr;
            _translationExpr = translationExpr;
            _scaleExpr = scaleExpr;
            _attached = true;
            _offsetFrozen = false;
            _scaleFrozen = false;
        }

        public bool Detach(bool resetVisual)
        {
            var now = Clock.GetTimestamp();
            var hadNonIdentity = Amount != 0
                || TargetOffset != Vector2.Zero
                || _offsetTarget != Vector2.Zero
                || _scaleTarget != 1
                || _offsetAnimating
                || _scaleAnimating
                || EvaluateOffset(now) != Vector2.Zero
                || Math.Abs(EvaluateScale(now) - 1) > 1e-9;

            if (resetVisual && _attached)
            {
                var visual = ElementCompositionPreview.GetElementVisual(Target);
                visual.StopAnimation(VisualTranslationProperty);
                visual.StopAnimation(VisualScaleProperty);
                visual.StopAnimation(VisualCenterPointProperty);
                visual.Properties.InsertVector3(VisualTranslationProperty, Vector3.Zero);
                visual.Scale = Vector3.One;
                visual.CenterPoint = Vector3.Zero;
            }

            if (_motionProps != null)
            {
                _motionProps.StopAnimation(MotionOffsetProperty);
                _motionProps.StopAnimation(MotionScaleProperty);
                _motionProps.StopAnimation(MotionClampedOffsetProperty);
                _motionProps.StopAnimation(MotionEffectiveScaleProperty);
                _motionProps.Dispose();
                _motionProps = null;
            }

            _clampedOffsetExpr?.Dispose();
            _clampedOffsetExpr = null;
            _effectiveScaleExpr?.Dispose();
            _effectiveScaleExpr = null;
            _translationExpr?.Dispose();
            _translationExpr = null;
            _scaleExpr?.Dispose();
            _scaleExpr = null;

            _compositor = null;
            _attached = false;
            _offsetFrozen = false;
            _scaleFrozen = false;
            Amount = 0;
            TargetOffset = Vector2.Zero;
            _offsetStart = Vector2.Zero;
            _offsetTarget = Vector2.Zero;
            _offsetAnimating = false;
            _scaleStart = 1;
            _scaleTarget = 1;
            _scaleAnimating = false;
            return hadNonIdentity;
        }

        public bool SetStrength(
            double strength,
            long now,
            bool canAnimate,
            CubicBezierEasingFunction? easing)
        {
            var newAmount = ParallaxMotionMath.GetAmount(strength);
            if (newAmount == 0)
            {
                var hadState = Amount != 0
                    || TargetOffset != Vector2.Zero
                    || _offsetTarget != Vector2.Zero
                    || _scaleTarget != 1
                    || _offsetAnimating
                    || _scaleAnimating;
                SnapToIdentity();
                return hadState;
            }

            var amountChanged = Amount != newAmount;
            Amount = newAmount;
            _motionProps?.InsertScalar(MotionMaxAmountProperty, (float)newAmount);

            var targetScale = 1 + newAmount;
            if (!canAnimate)
            {
                return amountChanged;
            }

            return StartScaleAnimation(targetScale, now, easing) || amountChanged;
        }

        public bool ResumeScale(
            double strength,
            long now,
            CubicBezierEasingFunction? easing)
        {
            var newAmount = ParallaxMotionMath.GetAmount(strength);
            Amount = newAmount;
            if (newAmount == 0)
            {
                SnapToIdentity();
                return false;
            }

            _motionProps?.InsertScalar(MotionMaxAmountProperty, (float)newAmount);
            return StartScaleAnimation(1 + newAmount, now, easing);
        }

        public bool UpdateTarget(
            Point? pointerInScene,
            double width,
            double height,
            long now,
            bool canAnimate,
            CubicBezierEasingFunction? easing,
            bool clampStartToViewport = false)
        {
            var newTarget = ParallaxMotionMath.CalculateTargetOffset(pointerInScene, width, height, Amount);
            var targetChanged = TargetOffset != newTarget;
            TargetOffset = newTarget;

            if (!canAnimate)
            {
                return targetChanged;
            }

            return StartOffsetAnimation(newTarget, width, height, now, easing, clampStartToViewport) || targetChanged;
        }

        public void Freeze(long now, ExpressionAnimation? freezeExpression)
        {
            if (!_attached || _motionProps == null || Amount == 0)
            {
                return;
            }

            if (!_offsetFrozen)
            {
                _offsetFrozen = true;
                var currentOffset = EvaluateOffset(now);
                _offsetStart = currentOffset;
                _offsetTarget = currentOffset;
                _offsetAnimating = false;
                if (freezeExpression != null)
                {
                    _motionProps.StartAnimation(MotionOffsetProperty, freezeExpression);
                }
            }

            if (!_scaleFrozen)
            {
                _scaleFrozen = true;
                var currentScale = EvaluateScale(now);
                _scaleStart = currentScale;
                _scaleTarget = currentScale;
                _scaleAnimating = false;
                if (freezeExpression != null)
                {
                    _motionProps.StartAnimation(MotionScaleProperty, freezeExpression);
                }
            }
        }

        public bool ResetMotion(long now, ExpressionAnimation? freezeExpression)
        {
            var currentOffset = EvaluateOffset(now);
            var hadMotion = TargetOffset != Vector2.Zero
                || _offsetTarget != Vector2.Zero
                || currentOffset != Vector2.Zero
                || _offsetAnimating
                || _scaleAnimating;

            TargetOffset = Vector2.Zero;
            _offsetStart = Vector2.Zero;
            _offsetTarget = Vector2.Zero;
            _offsetAnimating = false;
            _offsetFrozen = false;

            if (_motionProps != null)
            {
                _motionProps.StopAnimation(MotionOffsetProperty);
                _motionProps.InsertVector2(MotionOffsetProperty, Vector2.Zero);
            }

            var currentScale = EvaluateScale(now);
            _scaleStart = currentScale;
            _scaleTarget = currentScale;
            _scaleAnimating = false;

            if (_motionProps != null && freezeExpression != null && Amount > 0)
            {
                _scaleFrozen = true;
                _motionProps.StartAnimation(MotionScaleProperty, freezeExpression);
            }

            return hadMotion;
        }

        public BrushEditorMatrix EvaluateMatrix(
            long now,
            double viewportWidth,
            double viewportHeight,
            double targetWidth,
            double targetHeight,
            double multiplier)
        {
            var offset = EvaluateOffset(now);
            var scale = EvaluateScale(now);
            var (translation, effectiveScale) = ParallaxMotionMath.EvaluateEffectiveTransform(
                offset,
                scale,
                Amount,
                viewportWidth,
                viewportHeight,
                multiplier);
            return ParallaxMotionMath.CreateCenteredTransformMatrix(
                targetWidth,
                targetHeight,
                translation,
                effectiveScale);
        }

        private void SnapToIdentity()
        {
            Amount = 0;
            TargetOffset = Vector2.Zero;
            _offsetStart = Vector2.Zero;
            _offsetTarget = Vector2.Zero;
            _offsetAnimating = false;
            _offsetFrozen = false;
            _scaleStart = 1;
            _scaleTarget = 1;
            _scaleAnimating = false;
            _scaleFrozen = false;

            if (_motionProps != null)
            {
                _motionProps.StopAnimation(MotionOffsetProperty);
                _motionProps.StopAnimation(MotionScaleProperty);
                _motionProps.InsertVector2(MotionOffsetProperty, Vector2.Zero);
                _motionProps.InsertScalar(MotionScaleProperty, 1.0f);
                _motionProps.InsertScalar(MotionMaxAmountProperty, 0.0f);
            }
        }

        private bool StartScaleAnimation(
            double targetScale,
            long now,
            CubicBezierEasingFunction? easing)
        {
            var currentScale = EvaluateScale(now);
            var wasFrozen = _scaleFrozen;
            _scaleFrozen = false;

            if (Math.Abs(currentScale - targetScale) <= 1e-9 && (!wasFrozen || !_scaleAnimating))
            {
                if (_scaleAnimating || Math.Abs(_scaleTarget - targetScale) > 1e-9 || wasFrozen)
                {
                    _scaleStart = targetScale;
                    _scaleTarget = targetScale;
                    _scaleAnimating = false;
                    if (_motionProps != null)
                    {
                        _motionProps.StopAnimation(MotionScaleProperty);
                        _motionProps.InsertScalar(MotionScaleProperty, (float)targetScale);
                    }

                    return true;
                }

                return false;
            }

            if (!wasFrozen && _scaleAnimating && Math.Abs(_scaleTarget - targetScale) <= 1e-9)
            {
                return false;
            }

            _scaleStart = currentScale;
            _scaleTarget = targetScale;
            _scaleStartTimestamp = now;
            _scaleAnimating = true;

            if (_motionProps != null && _compositor != null && easing != null)
            {
                using var animation = _compositor.CreateScalarKeyFrameAnimation();
                animation.Duration = ParallaxMotionMath.ScaleDuration;
                // 显式写入解析起点关键帧，避免高刷显示器或高回报率输入连续重设目标时 Composition 反复锁死 this.StartingValue。
                animation.InsertKeyFrame(0.0f, (float)currentScale);
                animation.InsertKeyFrame(1.0f, (float)targetScale, easing);
                _motionProps.StartAnimation(MotionScaleProperty, animation);
            }

            return true;
        }

        private bool StartOffsetAnimation(
            Vector2 targetOffset,
            double width,
            double height,
            long now,
            CubicBezierEasingFunction? easing,
            bool clampStartToViewport)
        {
            var currentOffset = EvaluateOffset(now);
            var wasFrozen = _offsetFrozen;
            _offsetFrozen = false;

            var startOffset = currentOffset;
            if (clampStartToViewport && width > 0 && height > 0)
            {
                var currentScale = EvaluateScale(now);
                var margin = Math.Max(currentScale - 1, Amount) * 0.5;
                var limitX = (float)(width * margin);
                var limitY = (float)(height * margin);
                startOffset = new Vector2(
                    Math.Clamp(startOffset.X, -limitX, limitX),
                    Math.Clamp(startOffset.Y, -limitY, limitY));
            }

            if (startOffset == targetOffset && (!wasFrozen || !_offsetAnimating))
            {
                if (_offsetAnimating || _offsetTarget != targetOffset || currentOffset != targetOffset || wasFrozen)
                {
                    _offsetStart = targetOffset;
                    _offsetTarget = targetOffset;
                    _offsetAnimating = false;
                    if (_motionProps != null)
                    {
                        _motionProps.StopAnimation(MotionOffsetProperty);
                        _motionProps.InsertVector2(MotionOffsetProperty, targetOffset);
                    }

                    return true;
                }

                return false;
            }

            if (!wasFrozen && !clampStartToViewport && _offsetAnimating && _offsetTarget == targetOffset)
            {
                return false;
            }

            _offsetStart = startOffset;
            _offsetTarget = targetOffset;
            _offsetStartTimestamp = now;
            _offsetAnimating = true;

            if (_motionProps != null && _compositor != null && easing != null)
            {
                using var animation = _compositor.CreateVector2KeyFrameAnimation();
                animation.Duration = ParallaxMotionMath.PositionDuration;
                // 显式写入已完成视口裁剪的解析起点关键帧，防止帧内/跨帧高频 retargeting 时 Composition 反复锁回 0 进度。
                animation.InsertKeyFrame(0.0f, startOffset);
                animation.InsertKeyFrame(1.0f, targetOffset, easing);
                _motionProps.StartAnimation(MotionOffsetProperty, animation);
            }

            return true;
        }

        private Vector2 EvaluateOffset(long now)
        {
            if (!_offsetAnimating)
            {
                return _offsetTarget;
            }

            var elapsed = Clock.GetElapsedTime(_offsetStartTimestamp, now);
            if (elapsed <= TimeSpan.Zero)
            {
                return _offsetStart;
            }

            if (elapsed >= ParallaxMotionMath.PositionDuration)
            {
                _offsetStart = _offsetTarget;
                _offsetAnimating = false;
                return _offsetTarget;
            }

            var progress = elapsed.TotalMilliseconds / ParallaxMotionMath.PositionDuration.TotalMilliseconds;
            var eased = ParallaxMotionMath.EvaluateCubicBezier(
                progress,
                ParallaxMotionMath.OutQuintControlPoint1,
                ParallaxMotionMath.OutQuintControlPoint2);
            return Vector2.Lerp(_offsetStart, _offsetTarget, (float)eased);
        }

        private double EvaluateScale(long now)
        {
            if (!_scaleAnimating)
            {
                return _scaleTarget;
            }

            var elapsed = Clock.GetElapsedTime(_scaleStartTimestamp, now);
            if (elapsed <= TimeSpan.Zero)
            {
                return _scaleStart;
            }

            if (elapsed >= ParallaxMotionMath.ScaleDuration)
            {
                _scaleStart = _scaleTarget;
                _scaleAnimating = false;
                return _scaleTarget;
            }

            var progress = elapsed.TotalMilliseconds / ParallaxMotionMath.ScaleDuration.TotalMilliseconds;
            var eased = ParallaxMotionMath.EvaluateCubicBezier(
                progress,
                ParallaxMotionMath.OutQuintControlPoint1,
                ParallaxMotionMath.OutQuintControlPoint2);
            return _scaleStart + (_scaleTarget - _scaleStart) * eased;
        }
    }
}
