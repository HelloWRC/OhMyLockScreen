// The production host, API, collection and lifetime run against these declarations.
// Native pointer handling, acrylic resources and animation are verified by the UWP build.
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.UI;

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
}

namespace Windows.UI.Xaml.Controls.Primitives
{
    public class ButtonBase : Windows.UI.Xaml.Controls.Control;
}

namespace Windows.UI.Core
{
    public sealed class CoreWindowEventArgs : EventArgs;
}

namespace Windows.ApplicationModel.Core
{
    public sealed class CoreApplicationView
    {
        public CoreApplicationViewTitleBar TitleBar { get; } = new();
    }

    public sealed class CoreApplicationViewTitleBar
    {
        public bool ExtendViewIntoTitleBar { get; set; }
        public bool IsVisible { get; set; } = true;
        public double Height { get; set; } = 32;
    }
}

namespace Windows.UI.Xaml.Navigation
{
    public sealed class NavigationEventArgs : EventArgs;
}

namespace Windows.UI.Xaml
{
    public enum ElementTheme { Default, Light, Dark }
    public sealed class XamlRoot
    {
        public UIElement? Content { get; set; }
    }
}

namespace Windows.UI.Xaml.Media
{
    public sealed class GeneralTransform
    {
        public Windows.Foundation.Point TransformPoint(Windows.Foundation.Point point) => point;
    }

    public static class VisualTreeHelper
    {
        public static Windows.UI.Xaml.DependencyObject? GetParent(Windows.UI.Xaml.DependencyObject element)
        {
            if (element.VisualParent != null) return element.VisualParent;
            if (Windows.UI.Xaml.Window.Current.Content is Windows.UI.Xaml.Controls.Panel panel &&
                element is Windows.UI.Xaml.UIElement child && panel.Children.Contains(child)) return panel;
            return null;
        }
    }
}

namespace Windows.UI.Xaml.Controls
{
    public enum ScrollMode { Disabled, Auto }
    public enum ScrollBarVisibility { Disabled, Auto }

    public sealed class ScrollViewer : Control
    {
        public object? Content { get; set; }
        public ScrollMode HorizontalScrollMode { get; set; }
        public ScrollMode VerticalScrollMode { get; set; }
        public ScrollBarVisibility HorizontalScrollBarVisibility { get; set; }
        public ScrollBarVisibility VerticalScrollBarVisibility { get; set; }
        public bool IsTabStop { get; set; }
        public bool ChangeView(double? horizontal, double? vertical, float? zoom, bool disableAnimation) => true;
    }
}

namespace OhMyLockScreen.Shared.Controls.UI
{
    internal sealed class ToastCard : Windows.UI.Xaml.Controls.Grid, IDisposable
    {
        private readonly ToastLifetime _lifetime;
        internal ToastMessage Message { get; }
        internal event EventHandler? Removed;
        internal bool Disposed { get; private set; }

        internal ToastCard(ToastMessage message, AppToastAdorner adorner)
        {
            Message = message;
            _lifetime = new ToastLifetime(message, TimeProvider.System, action => action());
            message.Closed += OnClosed;
        }

        private void OnClosed(object? sender, EventArgs args) => Removed?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
            Disposed = true;
            _lifetime.Dispose();
            Message.Closed -= OnClosed;
        }
    }
}
