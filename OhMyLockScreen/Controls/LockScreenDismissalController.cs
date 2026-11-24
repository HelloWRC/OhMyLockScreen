using System.Diagnostics;
using OhMyLockScreen.Shared.Controls.Scene;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;

namespace OhMyLockScreen.Controls;

/// <summary>共享锁屏与预览页的指针手势、键盘兜底及解锁退场动画控制。</summary>
internal sealed class LockScreenDismissalController
{
    private const double DragSlop = 8;
    private const double MinimumFlickDistance = 12;
    private const double MinimumFlickVelocity = 600;
    private const ulong VelocityWindowMicroseconds = 100_000;

    private readonly FrameworkElement _host;
    private readonly ScenePresenter _presenter;
    private readonly Action? _onPreparingDismissal;
    private readonly Action<Task>? _onAnimationStarted;
    private readonly Func<Task>? _onCompleteDismissalAsync;
    private readonly Queue<(ulong Timestamp, double Y)> _pointerSamples = new();

    private Pointer? _dismissalPointer;
    private Point _pressPosition;
    private bool _exceededDragSlop;
    private bool _isDraggingUp;
    private CoreDispatcher? _viewDispatcher;
    private bool _isAttached;
    private bool _isDismissing;
    private bool _hasCompletedDismissal;
    private int _inputGeneration;

    internal bool IsDismissalStarted => _isDismissing || _hasCompletedDismissal;

    internal LockScreenDismissalController(
        FrameworkElement host,
        ScenePresenter presenter,
        Action? onPreparingDismissal = null,
        Action<Task>? onAnimationStarted = null,
        Func<Task>? onCompleteDismissalAsync = null)
    {
        _host = host;
        _presenter = presenter;
        _onPreparingDismissal = onPreparingDismissal;
        _onAnimationStarted = onAnimationStarted;
        _onCompleteDismissalAsync = onCompleteDismissalAsync;
    }

    internal void Attach(CoreDispatcher dispatcher)
    {
        if (_isAttached || _isDismissing)
        {
            return;
        }

        _isAttached = true;
        _viewDispatcher = dispatcher;

        // Ordinary bubbling subscriptions let component class handlers consume input first.
        _host.PointerPressed += OnPointerPressed;
        _host.PointerMoved += OnPointerMoved;
        _host.PointerReleased += OnPointerReleased;
        _host.PointerCanceled += OnPointerCanceled;
        _host.PointerCaptureLost += OnPointerCanceled;
        _host.KeyDown += OnKeyDown;
    }

    internal void Detach()
    {
        _isAttached = false;
        ++_inputGeneration;
        _host.PointerPressed -= OnPointerPressed;
        _host.PointerMoved -= OnPointerMoved;
        _host.PointerReleased -= OnPointerReleased;
        _host.PointerCanceled -= OnPointerCanceled;
        _host.PointerCaptureLost -= OnPointerCanceled;
        _host.KeyDown -= OnKeyDown;
        ReleaseDismissalPointer();
        _viewDispatcher = null;
    }

    internal async Task HandleUnloadedAsync()
    {
        Detach();
        if (_isDismissing)
        {
            // An unloaded view cannot be relied on to finish its storyboard.
            await CompleteDismissalAsync();
        }
        else
        {
            _presenter.ResetUnlockDrag();
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Handled || _dismissalPointer != null || _presenter.IsReturningFromUnlockDrag)
        {
            return;
        }

        var point = e.GetCurrentPoint(_host);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!_host.CapturePointer(e.Pointer))
        {
            return;
        }

        _dismissalPointer = e.Pointer;
        _presenter.SampleParallaxPointer(e);
        _pressPosition = point.Position;
        _pointerSamples.Enqueue((point.Timestamp, point.Position.Y));
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dismissalPointer?.PointerId != e.Pointer.PointerId)
        {
            return;
        }

        UpdateUnlockDrag(e.GetCurrentPoint(_host));
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dismissalPointer?.PointerId != e.Pointer.PointerId)
        {
            return;
        }

        var point = e.GetCurrentPoint(_host);
        double distance = UpdateUnlockDrag(point);
        var (timestamp, y) = _pointerSamples.Peek();
        ulong elapsed = point.Timestamp - timestamp;
        double velocity = elapsed > 0 ? (y - point.Position.Y) * 1_000_000 / elapsed : 0;
        double height = _presenter.ActualHeight;
        bool shouldDismiss = !_exceededDragSlop ||
            (_isDraggingUp && height > 0 && (distance >= height / 8 ||
                (distance >= MinimumFlickDistance && velocity >= MinimumFlickVelocity)));

        ReleaseDismissalPointer();
        e.Handled = true;
        if (shouldDismiss)
        {
            BeginDismissal();
        }
        else
        {
            _presenter.PlayUnlockReturnAnimation();
        }
    }

    private double UpdateUnlockDrag(PointerPoint point)
    {
        double x = point.Position.X - _pressPosition.X;
        double upwardDistance = _pressPosition.Y - point.Position.Y;
        _exceededDragSlop |= x * x + upwardDistance * upwardDistance > DragSlop * DragSlop;
        _isDraggingUp |= upwardDistance > DragSlop && upwardDistance > Math.Abs(x);
        if (_isDraggingUp)
        {
            _presenter.SetUnlockDragOffset(upwardDistance);
        }

        while (_pointerSamples.TryPeek(out var sample) && point.Timestamp - sample.Timestamp > VelocityWindowMicroseconds)
        {
            _pointerSamples.Dequeue();
        }

        _pointerSamples.Enqueue((point.Timestamp, point.Position.Y));
        return upwardDistance;
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_dismissalPointer?.PointerId != e.Pointer.PointerId)
        {
            return;
        }

        ReleaseDismissalPointer();
        _presenter.PlayUnlockReturnAnimation();
    }

    private void ReleaseDismissalPointer()
    {
        var pointer = _dismissalPointer;
        if (pointer == null)
        {
            return;
        }

        _dismissalPointer = null;
        _exceededDragSlop = false;
        _isDraggingUp = false;
        _pointerSamples.Clear();
        _host.ReleasePointerCapture(pointer);
    }

    private async void QueueUnhandledKey(KeyRoutedEventArgs args)
    {
        var dispatcher = _viewDispatcher;
        var generation = _inputGeneration;
        if (dispatcher == null)
        {
            return;
        }

        try
        {
            // Let the remaining route and application accelerators consume the key first.
            await dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                if (_isAttached && !_isDismissing && generation == _inputGeneration && !args.Handled)
                {
                    args.Handled = true;
                    BeginDismissal();
                }
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法调度锁屏按键处理：{exception}");
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Focus traversal and modifier chords finish after this event reaches the page.
        if (e.Handled || e.Key is VirtualKey.Tab
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            return;
        }

        QueueUnhandledKey(e);
    }

    private async void BeginDismissal()
    {
        if (!_isAttached || _isDismissing)
        {
            return;
        }

        _isDismissing = true;
        _onPreparingDismissal?.Invoke();
        Detach();
        try
        {
            var animation = _presenter.PlayUnlockAnimationAsync();
            _onAnimationStarted?.Invoke(animation);
            await animation;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法启动锁屏退出动画: {exception}");
        }

        await CompleteDismissalAsync();
    }

    private async Task CompleteDismissalAsync()
    {
        if (_hasCompletedDismissal)
        {
            return;
        }

        _hasCompletedDismissal = true;
        Detach();
        if (_onCompleteDismissalAsync != null)
        {
            await _onCompleteDismissalAsync();
        }
    }
}
