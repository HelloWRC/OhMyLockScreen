// Only used by this portable regression harness. No UWP package or real lock host is activated.
namespace Windows
{
    public delegate void UwpEventHandler<TArgs>(object sender, TArgs args);
}

namespace Windows.ApplicationModel.Activation
{
    public enum ActivationKind { Launch, LockScreen, LockScreenComponent, Other }
    public interface IActivatedEventArgs { ActivationKind Kind { get; } }
    public sealed class LaunchActivatedEventArgs : IActivatedEventArgs
    {
        public ActivationKind Kind => ActivationKind.Launch;
        // Hosted activations may not expose these interfaces. Access is a regression.
        public string Arguments => throw new InvalidCastException("Launch interface unavailable");
        public bool PrelaunchActivated => throw new InvalidCastException("Prelaunch interface unavailable");
    }
    public sealed record ActivatedEventArgs(ActivationKind Kind) : IActivatedEventArgs;
}

namespace Windows.ApplicationModel
{
    public sealed class SuspendingEventArgs : EventArgs
    {
        public SuspendingOperation SuspendingOperation { get; } = new();
    }
    public sealed class SuspendingOperation
    {
        public SuspendingDeferral GetDeferral() => new();
    }
    public sealed class SuspendingDeferral { public void Complete() { } }
}

namespace Windows.ApplicationModel.Core
{
    public sealed class CoreApplicationView
    {
        public bool IsHosted { get; init; }
        public bool IsMain { get; init; } = true;
    }
    public static class CoreApplication
    {
        public static Func<CoreApplicationView> CurrentViewFactory { get; set; } = () => new();
        public static CoreApplicationView GetCurrentView() => CurrentViewFactory();
        public static int ExitCalls { get; set; }
        public static void Exit() => ExitCalls++;
    }
}

namespace Windows.UI.ViewManagement
{
    public sealed class ApplicationView
    {
        public int Id { get; init; } = 1;
        public bool IsOnLockScreen { get; init; }
        public static Func<ApplicationView> CurrentViewFactory { get; set; } = () => new();
        public static ApplicationView GetForCurrentView() => CurrentViewFactory();
    }
}

namespace Windows.UI.Core
{
    public enum CoreWindowActivationState { Deactivated, CodeActivated, PointerActivated }
    public sealed class VisibilityChangedEventArgs(bool visible) : EventArgs
    {
        public bool Visible => visible;
    }
}

namespace Windows.UI
{
    public static class Colors { public static object Transparent { get; } = new(); }
}

namespace Windows.UI.Xaml.Media
{
    public sealed class SolidColorBrush(object color) { public object Color => color; }
}

namespace Windows.UI.Xaml.Navigation
{
    public sealed class NavigationFailedEventArgs(Type type) : EventArgs
    {
        public Type SourcePageType => type;
    }
}

namespace Windows.UI.Xaml
{
    using Windows.ApplicationModel;
    using Windows.ApplicationModel.Activation;
    using Windows.UI.Core;

    public class Application
    {
        public event UwpEventHandler<SuspendingEventArgs>? Suspending;
        protected virtual void OnLaunched(LaunchActivatedEventArgs args) { }
        protected virtual void OnActivated(IActivatedEventArgs args) { }
        public void Launch(LaunchActivatedEventArgs args) => OnLaunched(args);
        public void Activate(IActivatedEventArgs args) => OnActivated(args);
        public void Suspend() => Suspending?.Invoke(this, new());
    }
    public sealed class RoutedEventArgs : EventArgs;
    public sealed class WindowActivatedEventArgs(CoreWindowActivationState state) : EventArgs
    {
        public CoreWindowActivationState WindowActivationState => state;
    }
    public sealed class Window
    {
        [ThreadStatic] private static Window? current;
        public static Window Current { get => current ??= new(); set => current = value; }
        public object? Content { get; set; }
        public event UwpEventHandler<WindowActivatedEventArgs>? Activated;
        public event UwpEventHandler<VisibilityChangedEventArgs>? VisibilityChanged;
        public int ActivationSubscribers => Activated?.GetInvocationList().Length ?? 0;
        public int VisibilitySubscribers => VisibilityChanged?.GetInvocationList().Length ?? 0;
        public void Activate() => Activated?.Invoke(this, new(CoreWindowActivationState.CodeActivated));
        public void SetVisible(bool visible) => VisibilityChanged?.Invoke(this, new(visible));
        public void Deactivate() => Activated?.Invoke(this, new(CoreWindowActivationState.Deactivated));
    }
    public sealed class DispatcherTimer
    {
        public static DispatcherTimer? LastCreated { get; private set; }
        public DispatcherTimer() { Owner = Window.Current; LastCreated = this; }
        public Window Owner { get; }
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; private set; }
        public event UwpEventHandler<object>? Tick;
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void Advance()
        {
            if (!IsEnabled) return;
            if (Window.Current != Owner) throw new Exception("Retry left its owning view.");
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}

namespace Windows.UI.Xaml.Controls
{
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Navigation;
    public class Page
    {
        public event UwpEventHandler<RoutedEventArgs>? Loaded;
        public event UwpEventHandler<RoutedEventArgs>? Unloaded;
        public void Load() => Loaded?.Invoke(this, new());
        public void Unload() => Unloaded?.Invoke(this, new());
    }
    public sealed class Frame
    {
        public Windows.UI.Xaml.Media.SolidColorBrush? Background { get; set; }
        public object? Content { get; set; }
        public event UwpEventHandler<NavigationFailedEventArgs>? NavigationFailed;
        public bool Navigate(Type pageType)
        {
            try { Content = Activator.CreateInstance(pageType); return true; }
            catch { NavigationFailed?.Invoke(this, new(pageType)); return false; }
        }
    }
    public sealed class Button { public bool IsEnabled { get; set; } }
    public sealed class TextBlock { public string Text { get; set; } = string.Empty; }
}

namespace Windows.ApplicationModel.LockScreen
{
    public sealed class LockApplicationHost
    {
        public static Func<LockApplicationHost?> Acquire { get; set; } = () => null;
        public Action? Unlock { get; init; }
        public int UnlockRequests { get; private set; }
        public static LockApplicationHost? GetForCurrentView() => Acquire();
        public void RequestUnlock() { UnlockRequests++; Unlock?.Invoke(); }
    }
}

namespace OhMyLockScreen
{
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    internal static class WindowTransparency
    {
        internal static void Enable(Window window) { }
    }
    internal static partial class LockScreenHostAccess
    {
        internal static Func<Windows.ApplicationModel.LockScreen.LockApplicationHost?> BrokerAcquire { get; set; } = () => null;
        private static partial Windows.ApplicationModel.LockScreen.LockApplicationHost? CreateThroughBroker()
            => BrokerAcquire();
    }
    public partial class App { public void InitializeComponent() { } }
    public partial class MainPage
    {
        internal Button UnlockButton { get; } = new();
        internal TextBlock StatusText { get; } = new();
        internal TextBlock ViewStatusText { get; } = new();
        public void InitializeComponent() { }
        internal void ClickUnlock() => ButtonUnlock_Click(this, new RoutedEventArgs());
        internal void ClickExit() => ButtonExit_Click(this, new RoutedEventArgs());
    }
}
