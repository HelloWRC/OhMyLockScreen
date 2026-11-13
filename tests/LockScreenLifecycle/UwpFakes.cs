// Only used by this portable regression harness. No UWP package or real lock host is activated.
namespace Windows
{
    public delegate void UwpEventHandler<TArgs>(object sender, TArgs args);
    public delegate void UwpTypedEventHandler<TSender, TArgs>(TSender sender, TArgs args);
}

namespace Windows.Foundation
{
    public readonly record struct Point(double X, double Y);
    public readonly record struct Size(double Width, double Height);
    public readonly record struct Rect(double X, double Y, double Width, double Height);
}

namespace Windows.ApplicationModel.Activation
{
    public enum ActivationKind
    {
        Launch,
        LockScreen,
        LockScreenComponent,
        Other
    }

    public interface IActivatedEventArgs
    {
        ActivationKind Kind
        {
            get;
        }
    }

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
    public sealed class Package
    {
        public static Package Current { get; } = new();
        public PackageId Id { get; } = new();
        public Windows.Storage.StorageFolder InstalledLocation { get; } = new();
    }

    public sealed class PackageId
    {
        public PackageVersion Version { get; } = new();
    }

    public sealed class PackageVersion
    {
        public ushort Major => 1;
        public ushort Minor => 0;
        public ushort Build => 0;
        public ushort Revision => 0;
    }

    public static class FullTrustProcessLauncher
    {
        public static Task LaunchFullTrustProcessForCurrentAppAsync(string parameterGroupId) =>
            Task.CompletedTask;
    }

    public sealed class SuspendingEventArgs : EventArgs
    {
        public SuspendingOperation SuspendingOperation { get; } = new();
    }

    public sealed class SuspendingOperation
    {
        public SuspendingDeferral Deferral { get; } = new();

        public SuspendingDeferral GetDeferral() => Deferral;
    }

    public sealed class SuspendingDeferral
    {
        public bool IsComplete
        {
            get; private set;
        }

        public void Complete() => IsComplete = true;
    }
}

namespace Windows.Storage
{
    public sealed class StorageFile
    {
        public required string Path { get; init; }
        internal static Func<Uri, Task<StorageFile>>? OpenFromApplicationUri { get; set; }

        public static Task<StorageFile> GetFileFromApplicationUriAsync(Uri uri)
        {
            if (OpenFromApplicationUri != null)
            {
                return OpenFromApplicationUri(uri);
            }

            var path = global::System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, uri.AbsolutePath["/local/".Length..]);
            return Task.FromResult(new StorageFile { Path = global::System.IO.Path.GetFullPath(path) });
        }
    }

    public sealed class StorageFolder
    {
        public string Path { get; set; } = "";
    }

    public sealed class ApplicationData
    {
        public static ApplicationData Current { get; } = new();
        public StorageFolder LocalFolder { get; } = new();
    }
}

namespace Windows.ApplicationModel.Core
{
    public static class CoreApplication
    {
        private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<Windows.UI.Xaml.Window, CoreApplicationView> Views = new();
        public static CoreApplicationView GetCurrentView() => Views.GetValue(Windows.UI.Xaml.Window.Current, _ => new());
        public static int ExitCalls
        {
            get; set;
        }

        public static void Exit() => ExitCalls++;
    }
}

namespace Windows.UI.Core
{
    public enum CoreWindowActivationState
    {
        Deactivated,
        CodeActivated,
        PointerActivated
    }

    public enum CoreDispatcherPriority
    {
        Normal,
        Low
    }

    public enum CoreAcceleratorKeyEventType
    {
        KeyDown,
        KeyUp,
        SystemKeyDown,
        SystemKeyUp
    }

    public sealed class KeyEventArgs(Windows.System.VirtualKey key) : EventArgs
    {
        public Windows.System.VirtualKey VirtualKey => key;
        public bool Handled
        {
            get; set;
        }
    }

    public sealed class PointerEventArgs(Windows.Devices.Input.PointerDeviceType deviceType) : EventArgs
    {
        public Windows.Devices.Input.PointerDeviceType DeviceType => deviceType;
        public bool Handled
        {
            get; set;
        }
    }

    public sealed class AcceleratorKeyEventArgs(CoreAcceleratorKeyEventType eventType, Windows.System.VirtualKey key) : EventArgs
    {
        public CoreAcceleratorKeyEventType EventType => eventType;
        public Windows.System.VirtualKey VirtualKey => key;
        public bool Handled
        {
            get; set;
        }
    }

    public sealed class CoreDispatcher(Windows.UI.Xaml.Window? owner = null)
    {
        private readonly global::System.Collections.Concurrent.ConcurrentQueue<(Action Callback, TaskCompletionSource Completion)> _callbacks = new();
        public int PendingCallbacks => _callbacks.Count;
        public bool HasThreadAccess => owner == null || ReferenceEquals(owner, Windows.UI.Xaml.Window.Current);

        public Task RunAsync(CoreDispatcherPriority priority, Action callback)
        {
            var completion = new TaskCompletionSource();
            _callbacks.Enqueue((callback, completion));
            return completion.Task;
        }

        public void Drain()
        {
            if (!HasThreadAccess)
            {
                throw new InvalidOperationException("Dispatcher left its owning view.");
            }

            while (_callbacks.TryDequeue(out var work))
            {
                work.Callback();
                work.Completion.SetResult();
            }
        }

        public event UwpTypedEventHandler<CoreDispatcher, AcceleratorKeyEventArgs>? AcceleratorKeyActivated;
        public int AcceleratorSubscribers => AcceleratorKeyActivated?.GetInvocationList().Length ?? 0;

        public void PressAccelerator(CoreAcceleratorKeyEventType eventType, Windows.System.VirtualKey key) => AcceleratorKeyActivated?.Invoke(this, new(eventType, key));
    }

    public sealed partial class CoreWindow
    {
        public CoreDispatcher Dispatcher { get; } = new();

        public event UwpTypedEventHandler<CoreWindow, KeyEventArgs>? KeyDown;
        public event UwpTypedEventHandler<CoreWindow, PointerEventArgs>? PointerPressed;
        public event UwpTypedEventHandler<CoreWindow, PointerEventArgs>? PointerMoved;
        public int KeySubscribers => KeyDown?.GetInvocationList().Length ?? 0;
        public int PointerSubscribers => PointerPressed?.GetInvocationList().Length ?? 0;

        public void PressKey(Windows.System.VirtualKey key) => KeyDown?.Invoke(this, new(key));
        public Action CaptureKeyDown(Windows.System.VirtualKey key)
        {
            var handler = KeyDown;
            return () => handler?.Invoke(this, new(key));
        }

        public void PressPointer(Windows.Devices.Input.PointerDeviceType deviceType) => PointerPressed?.Invoke(this, new(deviceType));
        public void MovePointer() => PointerMoved?.Invoke(this, new(Windows.Devices.Input.PointerDeviceType.Mouse));
    }

    public sealed class VisibilityChangedEventArgs(bool visible) : EventArgs
    {
        public bool Visible => visible;
    }
}

namespace Windows.System
{
    public enum VirtualKey
    {
        A,
        Space,
        Enter,
        Escape,
        Shift,
        Control,
        Menu,
        Up,
        Down,
        Left,
        Right,
        Tab,
        LeftShift,
        RightShift,
        LeftControl,
        RightControl,
        LeftMenu,
        RightMenu,
        LeftWindows,
        RightWindows
    }
}

namespace Windows.Devices.Input
{
    public enum PointerDeviceType
    {
        Mouse,
        Touch,
        Pen
    }
}

namespace Windows.UI
{
    public static class Colors
    {
        public static Color White => Color.FromArgb(255, 255, 255, 255);
        public static Color Transparent => Color.FromArgb(0, 255, 255, 255);
    }
}

namespace Windows.UI.Xaml.Media
{
    public sealed class RectangleGeometry
    {
        public Windows.Foundation.Rect Rect { get; set; }
    }

    public class Transform : Windows.UI.Xaml.DependencyObject;
    public sealed class TransformGroup : Transform
    {
        public List<Transform> Children { get; } = [];
        public Matrix Value => Children.Aggregate(Matrix.Identity, (matrix, child) => matrix.Append(BrushFakeTransforms.GetMatrix(child)));
    }

    public sealed class TranslateTransform : Transform
    {
        public static readonly Windows.UI.Xaml.DependencyProperty XProperty = Windows.UI.Xaml.DependencyProperty.Register(nameof(X), typeof(double), typeof(TranslateTransform), new Windows.UI.Xaml.PropertyMetadata(0.0));
        public static readonly Windows.UI.Xaml.DependencyProperty YProperty = Windows.UI.Xaml.DependencyProperty.Register(nameof(Y), typeof(double), typeof(TranslateTransform), new Windows.UI.Xaml.PropertyMetadata(0.0));
        public double X
        {
            get => (double)GetValue(XProperty)!; set => SetValue(XProperty, value);
        }
        public double Y
        {
            get => (double)GetValue(YProperty)!; set => SetValue(YProperty, value);
        }
    }
}

namespace Windows.UI.Xaml.Media.Animation
{
    public enum FillBehavior
    {
        HoldEnd,
        Stop
    }

    public abstract class Timeline
    {
        public Duration Duration
        {
            get; set;
        }
        public FillBehavior FillBehavior
        {
            get; set;
        }
        internal object? Target
        {
            get; set;
        }
        internal string? TargetProperty
        {
            get; set;
        }

        internal abstract double GetValue(TimeSpan elapsed);
    }

    public sealed class DoubleAnimation : Timeline
    {
        public double? From
        {
            get; set;
        }
        public double? To
        {
            get; set;
        }

        internal override double GetValue(TimeSpan elapsed)
        {
            double progress = Math.Clamp(elapsed.TotalMilliseconds / Duration.TimeSpan.TotalMilliseconds, 0, 1);
            return From!.Value + (To!.Value - From.Value) * progress;
        }
    }

    public readonly record struct KeyTime(TimeSpan TimeSpan)
    {
        public static KeyTime FromTimeSpan(TimeSpan value) => new(value);
    }

    public sealed class KeySpline
    {
        public Windows.Foundation.Point ControlPoint1
        {
            get; set;
        }
        public Windows.Foundation.Point ControlPoint2
        {
            get; set;
        }

        internal double GetProgress(double progress)
        {
            static double Bezier(double t, double first, double second) => 3 * (1 - t) * (1 - t) * t * first + 3 * (1 - t) * t * t * second + t * t * t;
            double low = 0;
            double high = 1;
            for (int i = 0; i < 48; i++)
            {
                double middle = (low + high) / 2;
                if (Bezier(middle, ControlPoint1.X, ControlPoint2.X) < progress)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return Bezier((low + high) / 2, ControlPoint1.Y, ControlPoint2.Y);
        }
    }

    public abstract class DoubleKeyFrame
    {
        public KeyTime KeyTime
        {
            get; set;
        }
        public double Value
        {
            get; set;
        }
    }

    public sealed class DiscreteDoubleKeyFrame : DoubleKeyFrame;
    public sealed class SplineDoubleKeyFrame : DoubleKeyFrame
    {
        public KeySpline KeySpline { get; set; } = new();
    }

    public sealed class DoubleAnimationUsingKeyFrames : Timeline
    {
        public List<DoubleKeyFrame> KeyFrames { get; } = [];

        internal override double GetValue(TimeSpan elapsed)
        {
            double previousValue = 0;
            var previousTime = TimeSpan.Zero;
            foreach (var frame in KeyFrames)
            {
                if (elapsed < frame.KeyTime.TimeSpan)
                {
                    if (frame is not SplineDoubleKeyFrame spline)
                    {
                        return previousValue;
                    }

                    double progress = (elapsed - previousTime).TotalMilliseconds / (frame.KeyTime.TimeSpan - previousTime).TotalMilliseconds;
                    return previousValue + (frame.Value - previousValue) * spline.KeySpline.GetProgress(progress);
                }

                previousValue = frame.Value;
                previousTime = frame.KeyTime.TimeSpan;
            }

            return previousValue;
        }
    }

    public sealed class Storyboard
    {
        public static Storyboard? LastCreated
        {
            get; set;
        }
        public static bool FailOnBegin
        {
            get; set;
        }

        public Storyboard() => LastCreated = this;
        public List<Timeline> Children { get; } = [];

        public event UwpEventHandler<object>? Completed;
        public int CompletedSubscribers => Completed?.GetInvocationList().Length ?? 0;
        public bool IsRunning
        {
            get; private set;
        }
        public int BeginCalls
        {
            get; private set;
        }

        private TimeSpan elapsed;
        public static void SetTarget(Timeline animation, object target) => animation.Target = target;
        public static void SetTargetProperty(Timeline animation, string property) => animation.TargetProperty = property;
        public void Begin()
        {
            BeginCalls++;
            if (FailOnBegin)
            {
                throw new InvalidOperationException("Simulated animation startup failure.");
            }

            IsRunning = true;
            Advance(TimeSpan.Zero);
        }

        public void Stop() => IsRunning = false;
        public void Advance(TimeSpan amount)
        {
            if (!IsRunning)
            {
                return;
            }

            elapsed += amount;
            foreach (var animation in Children)
            {
                double value = animation.GetValue(elapsed);
                animation.Target!.GetType().GetProperty(animation.TargetProperty!)!.SetValue(animation.Target, value);
            }

            if (Children.All(animation => elapsed >= animation.Duration.TimeSpan))
            {
                IsRunning = false;
                Completed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}

namespace Windows.UI.Xaml.Navigation
{
    public enum NavigationCacheMode
    {
        Disabled,
        Required,
        Enabled
    }

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

    public enum Visibility
    {
        Visible,
        Collapsed
    }

    public class Application
    {
        public Dictionary<object, object> Resources { get; } = [];

        public static Application? Current
        {
            get; set;
        }

        public Application() => Current = this;
        public event UwpEventHandler<SuspendingEventArgs>? Suspending;
        public event UwpEventHandler<object>? Resuming;
        public event UwpEventHandler<UnhandledExceptionEventArgs>? UnhandledException;
        public void Resume() => Resuming?.Invoke(this, EventArgs.Empty);
        protected virtual void OnLaunched(LaunchActivatedEventArgs args)
        {
        }

        protected virtual void OnActivated(IActivatedEventArgs args)
        {
        }

        public void Launch(LaunchActivatedEventArgs args) => OnLaunched(args);
        public void Activate(IActivatedEventArgs args) => OnActivated(args);
        public UnhandledExceptionEventArgs RaiseUnhandledException(Exception exception)
        {
            var args = new UnhandledExceptionEventArgs(exception);
            UnhandledException?.Invoke(this, args);
            return args;
        }

        public SuspendingEventArgs Suspend()
        {
            var args = new SuspendingEventArgs();
            Suspending?.Invoke(this, args);
            return args;
        }
    }

    public sealed class UnhandledExceptionEventArgs(Exception exception) : EventArgs
    {
        public Exception Exception { get; } = exception;
        public bool Handled { get; set; }
        public string Message => Exception.Message;
    }

    public sealed class RoutedEventArgs : EventArgs;
    public sealed class SizeChangedEventArgs : EventArgs;
    public readonly record struct Duration(TimeSpan TimeSpan);
    public sealed class WindowActivatedEventArgs(CoreWindowActivationState state) : EventArgs
    {
        public CoreWindowActivationState WindowActivationState => state;
    }

    public sealed class Window
    {
        [ThreadStatic]
        private static Window? current;
        public static Window Current
        {
            get => current ??= new(); set => current = value;
        }
        private object? _content;
        private int _wrongThreadContentAccesses;
        public bool EnforceThreadAccess { get; set; }
        public Exception? ContentAccessException { get; set; }
        public int WrongThreadContentAccesses => Volatile.Read(ref _wrongThreadContentAccesses);
        public object? Content
        {
            get
            {
                VerifyContentAccess();
                return _content;
            }
            set
            {
                VerifyContentAccess();
                _content = value;
            }
        }
        public CoreWindow CoreWindow { get; } = new();
        public CoreDispatcher Dispatcher { get; }

        public Window() => Dispatcher = new CoreDispatcher(this);

        private void VerifyContentAccess()
        {
            if (EnforceThreadAccess && !Dispatcher.HasThreadAccess)
            {
                Interlocked.Increment(ref _wrongThreadContentAccesses);
                throw new global::System.Runtime.InteropServices.COMException("Window.Content was accessed from another view.", unchecked((int)0x8001010E));
            }

            if (ContentAccessException != null)
            {
                throw ContentAccessException;
            }
        }
        public event UwpEventHandler<CoreWindowEventArgs>? Closed;
        public int ClosedSubscribers => Closed?.GetInvocationList().Length ?? 0;
        public void Close() => Closed?.Invoke(this, new());

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
        public static List<DispatcherTimer> Created { get; } = [];
        public static DispatcherTimer? LastCreated
        {
            get; private set;
        }

        public DispatcherTimer()
        {
            Owner = Window.Current;
            LastCreated = this;
            Created.Add(this);
        }

        public Window Owner
        {
            get;
        }
        public TimeSpan Interval
        {
            get; set;
        }
        public bool IsEnabled
        {
            get; private set;
        }

        public event UwpEventHandler<object>? Tick;
        public int TickSubscribers => Tick?.GetInvocationList().Length ?? 0;

        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void Advance()
        {
            if (!IsEnabled)
            {
                return;
            }

            if (Window.Current != Owner)
            {
                throw new Exception("Clock left its owning view.");
            }

            Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}

namespace Windows.UI.Xaml.Controls
{
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Navigation;

    public sealed class ExceptionRoutedEventArgs : EventArgs
    {
        public string ErrorMessage
        {
            get;
            init;
        } = "";
    }

    public class Page : FrameworkElement
    {
        public Frame? Frame { get; internal set; }
        public NavigationCacheMode NavigationCacheMode { get; set; }

        public Page()
        {
            ActualWidth = 1920;
            ActualHeight = 1080;
        }
    }

    public sealed class Frame : FrameworkElement
    {
        public List<object> BackStack { get; } = [];
        public object? Tag { get; set; }
        public Windows.UI.Xaml.Media.SolidColorBrush? Background
        {
            get; set;
        }
        public object? Content
        {
            get; set;
        }

        public event UwpEventHandler<NavigationFailedEventArgs>? NavigationFailed;
        public event UwpEventHandler<NavigationEventArgs>? Navigated;
        public bool Navigate(Type pageType)
        {
            try
            {
                Content = Activator.CreateInstance(pageType);
                if (Content is DependencyObject child) child.VisualParent = this;
                if (Content is Page page) page.Frame = this;
                Navigated?.Invoke(this, new());
                return true;
            }
            catch
            {
                NavigationFailed?.Invoke(this, new(pageType));
                return false;
            }
        }
    }

    public sealed class Border : FrameworkElement
    {
        public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
            nameof(BorderBrush), typeof(Windows.UI.Xaml.Media.Brush), typeof(Border), new PropertyMetadata(null));
        public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
            nameof(BorderThickness), typeof(Thickness), typeof(Border), new PropertyMetadata(default(Thickness), (_, args) =>
            {
                var value = (Thickness)args.NewValue!;
                ValidateGeometry(value.Left, value.Top, value.Right, value.Bottom);
            }));
        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
            nameof(CornerRadius), typeof(CornerRadius), typeof(Border), new PropertyMetadata(default(CornerRadius), (_, args) =>
            {
                var value = (CornerRadius)args.NewValue!;
                ValidateGeometry(value.TopLeft, value.TopRight, value.BottomRight, value.BottomLeft);
            }));
        public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
            nameof(Padding), typeof(Thickness), typeof(Border), new PropertyMetadata(default(Thickness), (_, args) =>
            {
                var value = (Thickness)args.NewValue!;
                ValidateGeometry(value.Left, value.Top, value.Right, value.Bottom);
            }));

        private static void ValidateGeometry(params double[] values)
        {
            if (values.Any(value => !double.IsFinite(value) || value < 0))
            {
                throw new ArgumentException("Border geometry must be finite and nonnegative.");
            }
        }

        public Windows.UI.Xaml.Media.Brush? BorderBrush
        {
            get => (Windows.UI.Xaml.Media.Brush?)GetValue(BorderBrushProperty);
            set => SetValue(BorderBrushProperty, value);
        }

        public Thickness BorderThickness
        {
            get => (Thickness)GetValue(BorderThicknessProperty)!;
            set => SetValue(BorderThicknessProperty, value);
        }

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty)!;
            set => SetValue(CornerRadiusProperty, value);
        }

        public Thickness Padding
        {
            get => (Thickness)GetValue(PaddingProperty)!;
            set => SetValue(PaddingProperty, value);
        }

        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
            nameof(Background), typeof(Windows.UI.Xaml.Media.Brush), typeof(Border), new PropertyMetadata(null));

        public Windows.UI.Xaml.Media.Brush? Background
        {
            get => (Windows.UI.Xaml.Media.Brush?)GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }
    }

    public sealed class Viewbox
    {
        public double MaxWidth
        {
            get; set;
        }
        public double MaxHeight
        {
            get; set;
        }
    }

    public sealed partial class TextBlock : FrameworkElement
    {
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Windows.UI.Xaml.Media.Brush), typeof(TextBlock), new PropertyMetadata(null));

        public Windows.UI.Xaml.Media.Brush? Foreground
        {
            get => (Windows.UI.Xaml.Media.Brush?)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(TextBlock), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(TextBlock), new PropertyMetadata(14.0));
        public string Text
        {
            get => (string)GetValue(TextProperty)!; set => SetValue(TextProperty, value);
        }
        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty)!; set => SetValue(FontSizeProperty, value);
        }
    }

    public sealed partial class RichTextBlock : FrameworkElement
    {
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Windows.UI.Xaml.Media.Brush), typeof(RichTextBlock), new PropertyMetadata(null));

        public Windows.UI.Xaml.Media.Brush? Foreground
        {
            get => (Windows.UI.Xaml.Media.Brush?)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }
    }
}

namespace OhMyLockScreen
{
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;

    internal static class WindowTransparency
    {
        internal static void Enable(Window window)
        {
        }
    }

    internal static class LockScreenHosting
    {
        internal static Func<bool> IsHosted { get; set; } = () => true;

        internal static bool IsWindowHostedByLockScreen() => IsHosted();
    }

    public partial class App
    {
        public void InitializeComponent()
        {
        }
    }

    public partial class MainPage
    {
        internal OhMyLockScreen.Shared.Controls.Scene.ScenePresenter RootScenePresenter { get; } = new();

        public void InitializeComponent()
        {
        }

    }
}
