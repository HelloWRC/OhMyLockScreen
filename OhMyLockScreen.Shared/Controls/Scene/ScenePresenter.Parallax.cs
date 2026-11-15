using OhMyLockScreen.Shared.Models.Scene;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ScenePresenter
{
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

    internal bool SuppressParallaxWithoutBackground { get; set; }

    private UIElement? _parallaxInput;
    private double _parallaxAmount;
    private Point? _parallaxPointer;
    private Point _parallaxTarget;
    private Point _parallaxOffset;
    private double _parallaxScale = 1;
    private double _parallaxMultiplier = 1;
    private TimeProvider _parallaxClock = TimeProvider.System;
    private DispatcherTimer? _parallaxTimer;
    private long _lastParallaxTick;

    private static void OnParallaxInputSurfaceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (ScenePresenter)sender;
        presenter.UpdateParallaxInput();
        presenter.UpdateParallaxTarget();
    }

    private void RefreshParallaxStrength()
    {
        var background = Scene?.Background;
        var strength = IsActive && background != null &&
            (!SuppressParallaxWithoutBackground || background.Mode != BackgroundMode.None)
            ? background.ParallaxStrength
            : 0;
        _parallaxAmount = DefaultParallaxAmount * (strength / 100);
        UpdateParallaxInput();
        if (_parallaxAmount == 0)
        {
            _parallaxScale = 1;
            ResetParallaxMotion();
            return;
        }

        UpdateParallaxViewport();
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

    private void OnParallaxPointerChanged(object sender, PointerRoutedEventArgs args) => SampleParallaxPointer(args);

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
        var margin = (_parallaxScale - 1) / 2;
        var limitX = BackgroundViewport.ActualWidth * margin;
        var limitY = BackgroundViewport.ActualHeight * margin;
        _parallaxOffset = new Point(
            Math.Clamp(_parallaxOffset.X, -limitX, limitX),
            Math.Clamp(_parallaxOffset.Y, -limitY, limitY));
        ApplyParallaxTransform();
        UpdateParallaxTarget();
    }

    private void UpdateParallaxTarget()
    {
        var width = BackgroundViewport.ActualWidth;
        var height = BackgroundViewport.ActualHeight;
        _parallaxTarget = default;
        if (_parallaxAmount > 0 && _parallaxInput != null && _parallaxPointer is { } pointer && width > 0 && height > 0)
        {
            var position = ReferenceEquals(_parallaxInput, SceneRoot)
                ? pointer
                : _parallaxInput.TransformToVisual(SceneRoot).TransformPoint(pointer);
            // osu!lazer's desktop UI fits a 1024 × 768 logical area inside the viewport.
            var viewportScale = Math.Min(width / 1024, height / 768);
            var halfWidth = width / 2;
            var halfHeight = height / 2;
            _parallaxTarget = new Point(
                CalculateParallaxOffset(position.X - halfWidth, halfWidth, viewportScale),
                CalculateParallaxOffset(position.Y - halfHeight, halfHeight, viewportScale));
        }

        if (IsActive && _parallaxTimer?.IsEnabled != true && _parallaxMultiplier == 1 && _unlockStoryboard == null && !IsParallaxSettled)
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

    private double CalculateParallaxOffset(double distance, double halfExtent, double viewportScale)
    {
        var saturation = 1 - Math.Pow(ParallaxSaturationBase, Math.Abs(distance) / viewportScale);
        return Math.CopySign(saturation * halfExtent * _parallaxAmount, distance);
    }

    private bool IsParallaxSettled =>
        _parallaxOffset.Equals(_parallaxTarget) && _parallaxScale == 1 + _parallaxAmount;

    private void OnParallaxTick(object? sender, object args)
    {
        if (_parallaxTimer?.IsEnabled != true)
        {
            return;
        }

        var now = _parallaxClock.GetTimestamp();
        var elapsed = Math.Clamp(_parallaxClock.GetElapsedTime(_lastParallaxTick, now).TotalMilliseconds, 0, ParallaxPositionDuration);
        _lastParallaxTick = now;

        var x = EaseParallax(_parallaxOffset.X, _parallaxTarget.X, elapsed / ParallaxPositionDuration);
        var y = EaseParallax(_parallaxOffset.Y, _parallaxTarget.Y, elapsed / ParallaxPositionDuration);
        var scale = EaseParallax(_parallaxScale, 1 + _parallaxAmount, elapsed / ParallaxScaleDuration);

        // A quick translation can outpace zoom while the video or brush fills the clipped viewport.
        var width = BackgroundViewport.ActualWidth;
        var height = BackgroundViewport.ActualHeight;
        if (width > 0 && height > 0)
        {
            scale = Math.Max(scale, 1 + 2 * Math.Max(Math.Abs(x) / width, Math.Abs(y) / height));
        }

        _parallaxOffset = new Point(x, y);
        _parallaxScale = scale;
        ApplyParallaxTransform();
        if (IsParallaxSettled)
        {
            StopParallaxTimer();
        }
    }

    private static double EaseParallax(double current, double target, double progress) =>
        Math.Abs(current - target) <= ParallaxEpsilon
            ? target
            : target + (current - target) * Math.Pow(1 - progress, 5);

    private void SetParallaxMultiplier(double multiplier)
    {
        if (_parallaxMultiplier != multiplier)
        {
            _parallaxMultiplier = multiplier;
            if (multiplier < 1)
            {
                StopParallaxTimer();
            }

            ApplyParallaxTransform();
            if (multiplier == 1)
            {
                UpdateParallaxTarget();
            }
        }
    }

    private void ApplyParallaxTransform()
    {
        var transform = BackgroundParallaxTransform;
        var scale = 1 + (_parallaxScale - 1) * _parallaxMultiplier;
        if (transform.ScaleX != scale)
        {
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }

        transform.TranslateX = _parallaxOffset.X * _parallaxMultiplier;
        transform.TranslateY = _parallaxOffset.Y * _parallaxMultiplier;
    }

    private void ResetParallaxMotion()
    {
        StopParallaxTimer();
        _parallaxTarget = default;
        _parallaxOffset = default;
        ApplyParallaxTransform();
    }

    private void StopParallaxTimer() => _parallaxTimer?.Stop();

    private void DetachParallaxTimer()
    {
        if (_parallaxTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnParallaxTick;
            _parallaxTimer = null;
        }
    }
}
