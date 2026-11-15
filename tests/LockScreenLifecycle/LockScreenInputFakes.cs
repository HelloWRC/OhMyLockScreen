using Windows.Devices.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Input;
using Windows.UI.Xaml.Input;

namespace Windows.UI.Input
{
    public sealed record PointerPoint(Point Position, ulong Timestamp, PointerPointProperties Properties);

    public sealed record PointerPointProperties(bool IsLeftButtonPressed);
}

namespace Windows.UI.Xaml.Input
{
    public sealed record Pointer(uint PointerId, PointerDeviceType PointerDeviceType);
    public delegate void PointerEventHandler(object sender, PointerRoutedEventArgs args);

    public sealed class PointerRoutedEventArgs(Pointer pointer, PointerPoint point, DependencyObject source) : EventArgs
    {
        public Pointer Pointer => pointer;
        public DependencyObject OriginalSource => source;
        public bool Handled { get; set; }

        public PointerPoint GetCurrentPoint(UIElement relativeTo) => point;
    }
}

namespace Windows.UI.Xaml
{
    public sealed class RoutedEvent;

    public partial class UIElement
    {
        public static readonly RoutedEvent PointerMovedEvent = new();
        private event PointerEventHandler? HandledPointerMoved;
        public event UwpEventHandler<PointerRoutedEventArgs>? PointerPressed;
        public event UwpEventHandler<PointerRoutedEventArgs>? PointerEntered;
        public event UwpEventHandler<PointerRoutedEventArgs>? PointerMoved;
        public event UwpEventHandler<PointerRoutedEventArgs>? PointerExited;
        public event UwpEventHandler<PointerRoutedEventArgs>? PointerReleased;
        public event UwpEventHandler<PointerRoutedEventArgs>? PointerCanceled;
        public event UwpEventHandler<PointerRoutedEventArgs>? PointerCaptureLost;
        public event UwpEventHandler<KeyRoutedEventArgs>? KeyDown;
        public int PointerSubscribers => PointerPressed?.GetInvocationList().Length ?? 0;
        public int KeySubscribers => KeyDown?.GetInvocationList().Length ?? 0;
        public Pointer? CapturedPointer { get; private set; }
        public bool FailPointerCapture { get; set; }

        public bool CapturePointer(Pointer pointer)
        {
            if (FailPointerCapture || CapturedPointer != null)
            {
                return false;
            }

            CapturedPointer = pointer;
            return true;
        }

        public void ReleasePointerCapture(Pointer pointer)
        {
            if (CapturedPointer?.PointerId == pointer.PointerId)
            {
                CapturedPointer = null;
                RaisePointer(PointerCaptureLost, pointer, default, 0);
            }
        }

        // A stand-in for input arriving at the page after component routing.
        // Actual Button/Slider class handling belongs to the native UWP runtime.
        public PointerRoutedEventArgs PressPointer(
            PointerDeviceType deviceType, bool handled = false, uint id = 1,
            Point position = default, ulong timestamp = 0, bool leftButton = true)
        {
            return RaisePointer(PointerPressed, new(id, deviceType), position, timestamp, leftButton, handled);
        }

        public void AddHandler(RoutedEvent routedEvent, PointerEventHandler handler, bool handledEventsToo)
        {
            if (routedEvent != PointerMovedEvent || !handledEventsToo)
            {
                throw new NotSupportedException();
            }

            HandledPointerMoved += handler;
        }

        public void RemoveHandler(RoutedEvent routedEvent, PointerEventHandler handler)
        {
            if (routedEvent != PointerMovedEvent)
            {
                throw new NotSupportedException();
            }

            HandledPointerMoved -= handler;
        }

        public PointerRoutedEventArgs MovePointer(Pointer pointer, Point position, ulong timestamp, bool handled = false)
        {
            var args = RaisePointer(PointerMoved, pointer, position, timestamp, handled: handled);
            HandledPointerMoved?.Invoke(this, args);
            return args;
        }

        public void ExitPointer(Pointer pointer, Point position) => RaisePointer(PointerExited, pointer, position, 0);

        public void EnterPointer(Pointer pointer, Point position) => RaisePointer(PointerEntered, pointer, position, 0);

        public PointerRoutedEventArgs ReleasePointer(Pointer pointer, Point position = default, ulong timestamp = 0) =>
            RaisePointer(PointerReleased, pointer, position, timestamp);

        public void CancelPointer(Pointer pointer) => RaisePointer(PointerCanceled, pointer, default, 0);

        private PointerRoutedEventArgs RaisePointer(
            UwpEventHandler<PointerRoutedEventArgs>? handler, Pointer pointer, Point position, ulong timestamp,
            bool leftButton = false, bool handled = false)
        {
            var args = new PointerRoutedEventArgs(pointer, new(position, timestamp, new(leftButton)), this) { Handled = handled };
            if (!args.Handled)
            {
                handler?.Invoke(this, args);
            }
            return args;
        }

        public KeyRoutedEventArgs PressKey(VirtualKey key, bool handled = false)
        {
            var args = new KeyRoutedEventArgs(key, this) { Handled = handled };
            if (!args.Handled)
            {
                KeyDown?.Invoke(this, args);
            }
            return args;
        }

        public Action CaptureKeyDown(VirtualKey key)
        {
            var handler = KeyDown;
            return () => handler?.Invoke(this, new KeyRoutedEventArgs(key, this));
        }
    }
}
