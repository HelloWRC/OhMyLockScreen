using OhMyLockScreen.Shared.Models.Scene;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ScenePresenter : ISceneParallaxHost
{
    private const double DefaultParallaxStrength = 100;
    private const double DefaultParallaxAmount = 0.02;
    private const double ParallaxSaturationBase = 0.999;
    private const double ParallaxPositionDuration = 100;
    private const double ParallaxScaleDuration = 1000;
    private const double ParallaxEpsilon = 0.001;
    private static readonly TimeSpan ParallaxTickInterval = TimeSpan.FromMilliseconds(1000d / 60);

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

    private ParallaxMotion _backgroundParallax = null!;
    private readonly List<LayerParallaxRegistration> _layerParallaxes = [];
    private UIElement? _parallaxInput;
    private Point? _parallaxPointer;
    private double _parallaxMultiplier = 1;
    private TimeProvider _parallaxClock = TimeProvider.System;
    private DispatcherTimer? _parallaxTimer;
    private long _lastParallaxTick;

    private void InitializeParallax()
    {
        _backgroundParallax = new ParallaxMotion(BackgroundParallaxTransform);
    }

    private static void OnParallaxInputSurfaceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (ScenePresenter)sender;
        presenter.UpdateParallaxInput();
        presenter.UpdateParallaxTarget();
    }

    ISceneParallaxRegistration ISceneParallaxHost.Register(CompositeTransform transform, double? strengthOverride)
    {
        ArgumentNullException.ThrowIfNull(transform);

        var registration = new LayerParallaxRegistration(
            this,
            new ParallaxMotion(transform),
            NormalizeStrengthOverride(strengthOverride));
        _layerParallaxes.Add(registration);

        var width = BackgroundViewport.ActualWidth;
        var height = BackgroundViewport.ActualHeight;
        var changed = registration.Motion.SetStrength(
            GetLayerParallaxStrength(registration.StrengthOverride),
            width,
            height,
            _parallaxMultiplier);
        registration.Motion.UpdateTarget(GetPointerInScene(width, height), width, height);
        EnsureParallaxTimer();
        if (changed)
        {
            RaiseLayerParallaxUpdated();
        }

        return registration;
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
        var width = BackgroundViewport.ActualWidth;
        var height = BackgroundViewport.ActualHeight;
        var changed = registration.Motion.SetStrength(
            GetLayerParallaxStrength(registration.StrengthOverride),
            width,
            height,
            _parallaxMultiplier);
        registration.Motion.UpdateTarget(GetPointerInScene(width, height), width, height);
        EnsureParallaxTimer();
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

        var changed = registration.Motion.ResetIdentity();
        if (IsParallaxSettled)
        {
            StopParallaxTimer();
        }

        if (changed)
        {
            RaiseLayerParallaxUpdated();
        }
    }

    private void RefreshParallaxStrength()
    {
        UpdateParallaxInput();

        var width = BackgroundViewport.ActualWidth;
        var height = BackgroundViewport.ActualHeight;
        _backgroundParallax.SetStrength(GetBackgroundParallaxStrength(), width, height, _parallaxMultiplier);

        var layerChanged = false;
        foreach (var entry in _layerParallaxes)
        {
            layerChanged |= entry.Motion.SetStrength(
                GetLayerParallaxStrength(entry.StrengthOverride),
                width,
                height,
                _parallaxMultiplier);
        }

        UpdateParallaxTarget();
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
        ResetParallaxMotion();
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

    internal void SampleParallaxPointer(PointerRoutedEventArgs args)
    {
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse || _parallaxInput == null)
        {
            return;
        }

        _parallaxPointer = args.GetCurrentPoint(_parallaxInput).Position;
        UpdateParallaxTarget();
    }

    private void UpdateParallaxViewport()
    {
        var width = BackgroundViewport.ActualWidth;
        var height = BackgroundViewport.ActualHeight;
        _backgroundParallax.ClampToViewport(width, height, _parallaxMultiplier);

        var layerChanged = false;
        foreach (var entry in _layerParallaxes)
        {
            layerChanged |= entry.Motion.ClampToViewport(width, height, _parallaxMultiplier);
        }

        UpdateParallaxTarget();
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
        var width = BackgroundViewport.ActualWidth;
        var height = BackgroundViewport.ActualHeight;
        var pointerInScene = GetPointerInScene(width, height);
        _backgroundParallax.UpdateTarget(pointerInScene, width, height);
        foreach (var entry in _layerParallaxes)
        {
            entry.Motion.UpdateTarget(pointerInScene, width, height);
        }

        EnsureParallaxTimer();
    }

    private void EnsureParallaxTimer()
    {
        if (IsParallaxSettled)
        {
            StopParallaxTimer();
            return;
        }

        if (IsActive && _parallaxTimer?.IsEnabled != true && _parallaxMultiplier == 1 && _unlockStoryboard == null)
        {
            // A lock view can receive input before XAML requests a new frame.
            if (_parallaxTimer == null)
            {
                _parallaxTimer = new DispatcherTimer { Interval = ParallaxTickInterval };
                _parallaxTimer.Tick += OnParallaxTick;
            }

            _lastParallaxTick = _parallaxClock.GetTimestamp();
            _parallaxTimer.Start();
        }
    }

    private bool IsParallaxSettled
    {
        get
        {
            if (!_backgroundParallax.IsSettled)
            {
                return false;
            }

            foreach (var entry in _layerParallaxes)
            {
                if (!entry.Motion.IsSettled)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private void OnParallaxTick(object? sender, object args)
    {
        if (_parallaxTimer?.IsEnabled != true)
        {
            return;
        }

        var now = _parallaxClock.GetTimestamp();
        var elapsed = Math.Clamp(_parallaxClock.GetElapsedTime(_lastParallaxTick, now).TotalMilliseconds, 0, ParallaxPositionDuration);
        _lastParallaxTick = now;

        var width = BackgroundViewport.ActualWidth;
        var height = BackgroundViewport.ActualHeight;
        if (!_backgroundParallax.IsSettled)
        {
            _backgroundParallax.Step(elapsed, width, height, _parallaxMultiplier);
        }

        var layerChanged = false;
        foreach (var entry in _layerParallaxes)
        {
            if (!entry.Motion.IsSettled)
            {
                layerChanged |= entry.Motion.Step(elapsed, width, height, _parallaxMultiplier);
            }
        }

        if (layerChanged)
        {
            RaiseLayerParallaxUpdated();
        }

        if (IsParallaxSettled)
        {
            StopParallaxTimer();
        }
    }

    private void SetParallaxMultiplier(double multiplier)
    {
        if (_parallaxMultiplier == multiplier)
        {
            return;
        }

        _parallaxMultiplier = multiplier;
        if (multiplier < 1)
        {
            StopParallaxTimer();
        }

        _backgroundParallax.Apply(multiplier);
        var layerChanged = false;
        foreach (var entry in _layerParallaxes)
        {
            layerChanged |= entry.Motion.Apply(multiplier);
        }

        if (layerChanged)
        {
            RaiseLayerParallaxUpdated();
        }

        if (multiplier == 1)
        {
            UpdateParallaxTarget();
        }
    }

    private void ResetParallaxMotion()
    {
        StopParallaxTimer();
        _backgroundParallax.ResetMotion(_parallaxMultiplier);

        var layerChanged = false;
        foreach (var entry in _layerParallaxes)
        {
            layerChanged |= entry.Motion.ResetMotion(_parallaxMultiplier);
        }

        if (layerChanged)
        {
            RaiseLayerParallaxUpdated();
        }
    }

    private void AddParallaxUnlockAnimations(Storyboard storyboard)
    {
        AddParallaxUnlockAnimations(storyboard, BackgroundParallaxTransform);
        foreach (var entry in _layerParallaxes)
        {
            AddParallaxUnlockAnimations(storyboard, entry.Motion.Transform);
        }
    }

    private static void AddParallaxUnlockAnimations(Storyboard storyboard, CompositeTransform parallax)
    {
        if (parallax.ScaleX == 1 && parallax.ScaleY == 1 && parallax.TranslateX == 0 && parallax.TranslateY == 0)
        {
            return;
        }

        AddDoubleAnimation(storyboard, parallax, nameof(CompositeTransform.ScaleX), parallax.ScaleX, 1, UnlockDuration);
        AddDoubleAnimation(storyboard, parallax, nameof(CompositeTransform.ScaleY), parallax.ScaleY, 1, UnlockDuration);
        AddDoubleAnimation(storyboard, parallax, nameof(CompositeTransform.TranslateX), parallax.TranslateX, 0, UnlockDuration);
        AddDoubleAnimation(storyboard, parallax, nameof(CompositeTransform.TranslateY), parallax.TranslateY, 0, UnlockDuration);
    }

    private void RaiseLayerParallaxUpdated()
    {
        LayerParallaxUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void StopParallaxTimer()
    {
        _parallaxTimer?.Stop();
    }

    private void DetachParallaxTimer()
    {
        if (_parallaxTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnParallaxTick;
            _parallaxTimer = null;
        }
    }

    private static double? NormalizeStrengthOverride(double? strengthOverride)
    {
        if (strengthOverride is not { } value)
        {
            return null;
        }

        return double.IsFinite(value)
            ? Math.Clamp(value, 0, 200)
            : DefaultParallaxStrength;
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

            var normalized = NormalizeStrengthOverride(strengthOverride);
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

    private sealed class ParallaxMotion(CompositeTransform transform)
    {
        public CompositeTransform Transform { get; } = transform;

        public double Amount { get; private set; }

        public Point Target { get; private set; }

        public Point Offset { get; private set; }

        public double Scale { get; private set; } = 1;

        public bool IsSettled => Offset.Equals(Target) && Scale == 1 + Amount;

        public bool SetStrength(double strength, double width, double height, double multiplier)
        {
            var clamped = double.IsFinite(strength) ? Math.Clamp(strength, 0, 200) : 0;
            Amount = DefaultParallaxAmount * (clamped / 100);
            if (Amount == 0)
            {
                Scale = 1;
                return ResetMotion(multiplier);
            }

            return ClampToViewport(width, height, multiplier);
        }

        public bool ClampToViewport(double width, double height, double multiplier)
        {
            var margin = (Scale - 1) / 2;
            var limitX = width * margin;
            var limitY = height * margin;
            Offset = new Point(
                Math.Clamp(Offset.X, -limitX, limitX),
                Math.Clamp(Offset.Y, -limitY, limitY));
            return Apply(multiplier);
        }

        public void UpdateTarget(Point? pointerInScene, double width, double height)
        {
            Target = default;
            if (Amount > 0 && pointerInScene is { } position && width > 0 && height > 0)
            {
                // osu!lazer's desktop UI fits a 1024 × 768 logical area inside the viewport.
                var viewportScale = Math.Min(width / 1024, height / 768);
                var halfWidth = width / 2;
                var halfHeight = height / 2;
                Target = new Point(
                    CalculateParallaxOffset(position.X - halfWidth, halfWidth, viewportScale),
                    CalculateParallaxOffset(position.Y - halfHeight, halfHeight, viewportScale));
            }
        }

        public bool Step(double elapsed, double width, double height, double multiplier)
        {
            var x = EaseParallax(Offset.X, Target.X, elapsed / ParallaxPositionDuration);
            var y = EaseParallax(Offset.Y, Target.Y, elapsed / ParallaxPositionDuration);
            var scale = EaseParallax(Scale, 1 + Amount, elapsed / ParallaxScaleDuration);

            // A quick translation can outpace zoom while the target fills the clipped viewport.
            if (width > 0 && height > 0)
            {
                scale = Math.Max(scale, 1 + 2 * Math.Max(Math.Abs(x) / width, Math.Abs(y) / height));
            }

            Offset = new Point(x, y);
            Scale = scale;
            return Apply(multiplier);
        }

        public bool Apply(double multiplier)
        {
            var scale = 1 + (Scale - 1) * multiplier;
            var translateX = Offset.X * multiplier;
            var translateY = Offset.Y * multiplier;
            var changed = false;

            if (Transform.ScaleX != scale)
            {
                Transform.ScaleX = scale;
                changed = true;
            }

            if (Transform.ScaleY != scale)
            {
                Transform.ScaleY = scale;
                changed = true;
            }

            if (Transform.TranslateX != translateX)
            {
                Transform.TranslateX = translateX;
                changed = true;
            }

            if (Transform.TranslateY != translateY)
            {
                Transform.TranslateY = translateY;
                changed = true;
            }

            return changed;
        }

        public bool ResetMotion(double multiplier)
        {
            Target = default;
            Offset = default;
            return Apply(multiplier);
        }

        public bool ResetIdentity()
        {
            Amount = 0;
            Scale = 1;
            Target = default;
            Offset = default;
            return Apply(1);
        }

        private double CalculateParallaxOffset(double distance, double halfExtent, double viewportScale)
        {
            var saturation = 1 - Math.Pow(ParallaxSaturationBase, Math.Abs(distance) / viewportScale);
            return Math.CopySign(saturation * halfExtent * Amount, distance);
        }

        private static double EaseParallax(double current, double target, double progress)
        {
            return Math.Abs(current - target) <= ParallaxEpsilon
                ? target
                : target + (current - target) * Math.Pow(1 - progress, 5);
        }
    }
}
