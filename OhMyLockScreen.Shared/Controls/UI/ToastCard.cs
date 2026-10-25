using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.UI;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>一条消息的输入、动画与计时生命周期。</summary>
internal sealed class ToastCard : Grid, IDisposable
{
    private readonly InfoBar _info;
    private readonly AppToastAdorner _adorner;
    private readonly Border _body;
    private readonly CompositeTransform _motion = new();
    private readonly TranslateTransform _entrance = new();
    private readonly ToastLifetime _lifetime;
    private readonly Queue<(long Time, double X)> _samples = [];
    private readonly PointerEventHandler _pressedHandler;
    private readonly PointerEventHandler _movedHandler;
    private readonly PointerEventHandler _releasedHandler;
    private readonly PointerEventHandler _lostHandler;
    private UIElement? _root;
    private Pointer? _pointer;
    private Point _pressedAt;
    private double _startOffset;
    private bool _dragging;
    private bool _interacting;
    private bool _closing;
    private bool _disposed;
    private Action<double>? _animation;
    private Action? _animationCompleted;
    private long _animationStarted;
    private double _animationSeconds;

    internal ToastMessage Message { get; }
    internal event EventHandler? Removed;
    private static bool CanAnimate => new UISettings().AnimationsEnabled;

    internal ToastCard(ToastMessage message, AppToastAdorner adorner)
    {
        Message = message;
        _adorner = adorner;
        HorizontalAlignment = HorizontalAlignment.Right;
        RenderTransform = _entrance;
        _info = new InfoBar
        {
            Title = message.Title,
            Message = message.Message,
            Severity = message.Severity,
            ActionButton = message.ActionContent,
            IsClosable = message.CanUserClose,
            IsOpen = true
        };
        _info.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("ms-appx:///OhMyLockScreen.Shared/Controls/UI/ToastResources.xaml")
        });
        _body = new Border
        {
            Child = _info,
            RenderTransform = _motion,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        Children.Add(_body);
        _lifetime = new ToastLifetime(message, TimeProvider.System, Dispatch);
        _pressedHandler = OnPressed;
        _movedHandler = OnMoved;
        _releasedHandler = OnReleased;
        _lostHandler = OnCaptureLost;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _info.CloseButtonClick += OnCloseButtonClick;
        _info.Closing += OnInfoClosing;
        _body.AddHandler(PointerPressedEvent, _pressedHandler, true);
        Message.Closed += OnMessageClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        if (!Message.IsOpen)
        {
            BeginClose();
            return;
        }

        _lifetime.Resume();
        if (CanAnimate)
        {
            _body.Opacity = 0;
            _entrance.Y = -25;
            Animate(0.3, progress =>
            {
                _body.Opacity = Ease(progress);
                _entrance.Y = -25 * (1 - Ease(progress));
            });
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => Dispose();

    private void OnCloseButtonClick(InfoBar sender, object args) => Message.Close();

    private void OnInfoClosing(InfoBar sender, InfoBarClosingEventArgs args) => args.Cancel = true;

    private void OnMessageClosed(object? sender, EventArgs args) => Dispatch(BeginClose);

    private void Dispatch(Action action)
    {
        if (_disposed)
        {
            return;
        }

        if (Dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (!_disposed)
                {
                    action();
                }
            });
        }
    }

    private void BeginClose()
    {
        if (_closing || _disposed)
        {
            return;
        }

        _closing = true;
        _lifetime.Pause();
        ReleasePointer();
        _body.IsHitTestVisible = false;
        // InfoBar normally collapses immediately when its close button is pressed.
        // Keep it open until our exit animation has finished.
        _info.IsOpen = true;
        _info.IsEnabled = false;
        var opacity = _body.Opacity;
        Animate(0.3, progress => _body.Opacity = opacity * (1 - progress), Remove);
    }

    private void Remove() => Removed?.Invoke(this, EventArgs.Empty);

    private bool IsInteractiveSource(DependencyObject? source)
    {
        for (var current = source; current != null && current != _body; current = VisualTreeHelper.GetParent(current))
        {
            if (current == Message.ActionContent || current is ButtonBase or TextBox or RichEditBox or PasswordBox or
                Selector or RangeBase or Thumb || current is Control { IsTabStop: true } and not InfoBar)
            {
                return true;
            }
        }

        return false;
    }

    private void OnPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_pointer != null || _closing || _disposed || !Message.IsOpen || IsInteractiveSource(args.OriginalSource as DependencyObject))
        {
            return;
        }

        var point = args.GetCurrentPoint(this);
        if (args.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Observe at the window root before capturing; ordinary clicks and vertical
        // scrolling must remain available until horizontal intent is established.
        _root = WindowToastHost.Find(this);
        _pointer = args.Pointer;
        _pressedAt = args.GetCurrentPoint(_root).Position;
        _samples.Clear();
        Sample(_pressedAt.X);
        _root.AddHandler(PointerMovedEvent, _movedHandler, true);
        _root.AddHandler(PointerReleasedEvent, _releasedHandler, true);
        _root.AddHandler(PointerCaptureLostEvent, _lostHandler, true);
        _root.AddHandler(PointerCanceledEvent, _lostHandler, true);
    }

    private void OnMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_pointer?.PointerId != args.Pointer.PointerId || !Message.IsOpen)
        {
            return;
        }

        var position = args.GetCurrentPoint(_root).Position;
        var x = position.X - _pressedAt.X;
        var y = position.Y - _pressedAt.Y;
        Sample(position.X);
        if (!_dragging)
        {
            if (ToastSwipeGesture.IsVertical(x, y))
            {
                ReleasePointer();
                return;
            }

            if (!ToastSwipeGesture.IsHorizontal(x, y))
            {
                return;
            }

            StopAnimation();
            _entrance.Y = 0;
            _body.Opacity = 1;
            _startOffset = Message.CanUserClose ? _motion.TranslateX : ToastSwipeGesture.Unresist(_motion.TranslateX, ActualWidth);
            _dragging = true;
            SetInteracting(true);
            _adorner.FloatBody(this, _body);
            if (!_body.CapturePointer(args.Pointer))
            {
                ReleasePointer();
                ReturnToRest();
                return;
            }
        }

        MoveTo(_startOffset + x);
        args.Handled = true;
    }

    private void MoveTo(double offset)
    {
        _motion.TranslateX = Message.CanUserClose ? offset : ToastSwipeGesture.Resist(offset, ActualWidth);
        _motion.Rotation = CanAnimate ? Math.Clamp(_motion.TranslateX * 0.08, -12, 0) : 0;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_pointer?.PointerId != args.Pointer.PointerId)
        {
            return;
        }

        var position = args.GetCurrentPoint(_root).Position;
        var velocity = Sample(position.X);
        var dragging = _dragging;
        if (dragging)
        {
            MoveTo(_startOffset + position.X - _pressedAt.X);
        }

        ReleasePointer();
        if (!dragging || !Message.IsOpen)
        {
            return;
        }

        args.Handled = true;
        if (ToastSwipeGesture.ShouldDismiss(_motion.TranslateX, velocity, ActualWidth, Message.CanUserClose))
        {
            Dismiss(velocity);
        }
        else
        {
            ReturnToRest();
        }
    }

    private double Sample(double x)
    {
        var now = Stopwatch.GetTimestamp();
        _samples.Enqueue((now, x));
        while (_samples.Count > 1 && Stopwatch.GetElapsedTime(_samples.Peek().Time, now).TotalMilliseconds > 100)
        {
            _samples.Dequeue();
        }

        var first = _samples.Peek();
        var seconds = Stopwatch.GetElapsedTime(first.Time, now).TotalSeconds;
        return seconds > 0 ? (x - first.X) / seconds : 0;
    }

    private void OnCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        if (_pointer?.PointerId != args.Pointer.PointerId)
        {
            return;
        }

        var dragging = _dragging;
        ReleasePointer();
        if (dragging && Message.IsOpen)
        {
            ReturnToRest();
        }
    }

    private void ReleasePointer()
    {
        var pointer = _pointer;
        _pointer = null;
        _dragging = false;
        if (_root != null)
        {
            _root.RemoveHandler(PointerMovedEvent, _movedHandler);
            _root.RemoveHandler(PointerReleasedEvent, _releasedHandler);
            _root.RemoveHandler(PointerCaptureLostEvent, _lostHandler);
            _root.RemoveHandler(PointerCanceledEvent, _lostHandler);
            _root = null;
        }

        if (pointer != null)
        {
            _body.ReleasePointerCapture(pointer);
        }
    }

    private void SetInteracting(bool interacting)
    {
        if (_interacting == interacting)
        {
            return;
        }

        _interacting = interacting;
        Canvas.SetZIndex(this, interacting ? 1 : 0);
        if (interacting)
        {
            _lifetime.Pause();
        }
        else
        {
            _lifetime.Resume();
        }
    }

    private void ReturnToRest()
    {
        var x = _motion.TranslateX;
        var angle = _motion.Rotation;
        Animate(0.3, progress =>
        {
            _motion.TranslateX = x * (1 - Ease(progress));
            _motion.Rotation = angle * (1 - Ease(progress));
        }, () =>
        {
            _adorner.RestoreBody(this, _body);
            SetInteracting(false);
        });
    }

    private void Dismiss(double velocity)
    {
        _closing = true;
        _body.IsHitTestVisible = false;
        Message.Close();
        var x = _motion.TranslateX;
        var angle = _motion.Rotation;
        var right = x > 0;
        var speed = Math.Clamp(Math.Abs(Math.Min(velocity, 0)), 800, 2400);
        var seconds = right ? 0.25 : 0.3;
        Animate(seconds, progress =>
        {
            var elapsed = progress * seconds;
            _motion.TranslateX = right ? x + (ActualWidth + 16) * Ease(progress) : x - speed * elapsed;
            _motion.TranslateY = right ? 0 : 1800 * elapsed * elapsed;
            _motion.Rotation = right ? angle * (1 - Ease(progress)) : angle - 60 * elapsed;
            _body.Opacity = 1 - progress * progress;
        }, Remove);
    }

    private void Animate(double seconds, Action<double> update, Action? completed = null)
    {
        StopAnimation();
        if (!CanAnimate)
        {
            update(1);
            completed?.Invoke();
            return;
        }

        _animation = update;
        _animationCompleted = completed;
        _animationSeconds = seconds;
        _animationStarted = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, object args)
    {
        var progress = CanAnimate ? Math.Clamp(Stopwatch.GetElapsedTime(_animationStarted).TotalSeconds / _animationSeconds, 0, 1) : 1;
        _animation?.Invoke(progress);
        if (progress < 1)
        {
            return;
        }

        var completed = _animationCompleted;
        StopAnimation();
        completed?.Invoke();
    }

    private void StopAnimation()
    {
        CompositionTarget.Rendering -= OnRendering;
        _animation = null;
        _animationCompleted = null;
    }

    private static double Ease(double progress) => 1 - Math.Pow(1 - progress, 3);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAnimation();
        ReleasePointer();
        _adorner.RestoreBody(this, _body);
        _interacting = false;
        _lifetime.Dispose();
        Message.Closed -= OnMessageClosed;
        _info.CloseButtonClick -= OnCloseButtonClick;
        _info.Closing -= OnInfoClosing;
        _info.ActionButton = null;
        _body.RemoveHandler(PointerPressedEvent, _pressedHandler);
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }
}
