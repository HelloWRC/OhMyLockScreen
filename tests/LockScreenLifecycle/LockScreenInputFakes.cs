using Windows.Devices.Input;
using Windows.System;
using Windows.UI.Xaml.Input;

namespace Windows.UI.Xaml.Input
{
    public sealed class PointerRoutedEventArgs(PointerDeviceType deviceType, DependencyObject source) : EventArgs
    {
        public PointerDeviceType DeviceType => deviceType;
        public DependencyObject OriginalSource => source;
        public bool Handled { get; set; }
    }
}

namespace Windows.UI.Xaml
{
    public partial class UIElement
    {
        public event UwpEventHandler<PointerRoutedEventArgs>? PointerPressed;
        public event UwpEventHandler<KeyRoutedEventArgs>? KeyDown;
        public int PointerSubscribers => PointerPressed?.GetInvocationList().Length ?? 0;
        public int KeySubscribers => KeyDown?.GetInvocationList().Length ?? 0;

        // A stand-in for input arriving at the page after component routing.
        // Actual Button/Slider class handling belongs to the native UWP runtime.
        public PointerRoutedEventArgs PressPointer(PointerDeviceType deviceType, bool handled = false)
        {
            var args = new PointerRoutedEventArgs(deviceType, this) { Handled = handled };
            if (!args.Handled)
            {
                PointerPressed?.Invoke(this, args);
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
