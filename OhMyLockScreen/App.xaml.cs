using System;
using System.Diagnostics;
using Windows.ApplicationModel;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media;
using Windows.UI.Core;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Views;

namespace OhMyLockScreen;
/// <summary>
/// Provides application-specific behavior to supplement the default <see cref = "Application"/> class.
/// </summary>
public sealed partial class App : Application
{
    private const string UnhandledExceptionToastTitle = "应用发生未处理异常";
    private readonly ConditionalWeakTable<Window, ViewActivation> _viewActivations = new();
    private readonly ConditionalWeakTable<Window, PendingUnhandledException> _pendingUnhandledExceptions = new();

    private sealed class ViewActivation
    {
        internal ActivationKind? Kind
        {
            get; set;
        }

        internal bool IsClosed { get; set; }
        internal DispatcherTimer? EditorWindowProbe { get; set; }
        internal string? EditorRecoveryRequest { get; set; }
    }

    private sealed class PendingUnhandledException
    {
        internal Exception? Exception { get; set; }
    }

    /// <summary>
        /// Initializes the singleton application object. This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App() : this(CreateHost)
    {
    }

    internal App(Func<IHost> hostFactory)
    {
        UnhandledException += OnUnhandledException;
        _initializeHost = new Lazy<Task>(() => StartHostAsync(hostFactory));
        InitializeComponent();
        Suspending += OnSuspending;
    }

    private void OnUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        // Keep the UI process alive so the user can see and dismiss the failure.
        args.Handled = true;

        try
        {
            LogLifecycleError(args.Exception, UnhandledExceptionToastTitle);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"记录未处理异常失败: {exception}");
        }

        try
        {
            ShowUnhandledExceptionToast(Window.Current, args.Exception);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"显示未处理异常提示失败: {exception}");
        }
    }

    private void ShowUnhandledExceptionToast(Window window, Exception exception)
    {
        if (window.Content is not WindowToastHost)
        {
            var pending = _pendingUnhandledExceptions.GetValue(
                window,
                currentWindow =>
                {
                    var state = new PendingUnhandledException();
                    currentWindow.Closed += (_, _) => state.Exception = null;
                    return state;
                });
            pending.Exception = exception;
            return;
        }

        window.ShowErrorToast(UnhandledExceptionToastTitle, exception);
    }

    private void ShowPendingUnhandledException(Window window)
    {
        if (!_pendingUnhandledExceptions.TryGetValue(window, out var pending) ||
            pending.Exception is not { } exception)
        {
            return;
        }

        pending.Exception = null;
        try
        {
            window.ShowErrorToast(UnhandledExceptionToastTitle, exception);
        }
        catch (Exception toastException)
        {
            Debug.WriteLine($"显示启动阶段未处理异常提示失败: {toastException}");
        }
    }

    /// <inheritdoc/>
    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        // A hosted launch can reach this callback without the launch-specific
        // interfaces (Arguments/PrelaunchActivated). Only the current view is needed.
        ShowMainPage();
    }

    protected override void OnActivated(IActivatedEventArgs args)
    {
        var kind = args.Kind;
        if (kind is not ActivationKind.LockScreenComponent and not ActivationKind.LockScreen)
        {
            base.OnActivated(args);
        }

        ShowMainPage(kind);
    }

    private async void ShowMainPage(ActivationKind? activationKind = null)
    {
        var window = Window.Current;
        var dispatcher = window.Dispatcher;
        // Keep the latest OnActivated value per view, including during host startup.
        var activation = _viewActivations.GetValue(window, currentWindow =>
        {
            var state = new ViewActivation();
            currentWindow.Closed += (_, _) =>
            {
                state.IsClosed = true;
                StopEditorWindowProbe(state);
            };
            return state;
        });
        activation.Kind = activationKind;
        try
        {
            await _initializeHost.Value.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogLifecycleError(exception, "应用主机启动失败");
            return;
        }

        if (_isExiting)
        {
            return;
        }

        try
        {
            // Lock-screen activation may have no SynchronizationContext to restore after await.
            // Use the captured window's dispatcher, since another view can share this host.
            if (dispatcher.HasThreadAccess)
            {
                ShowOnView();
            }
            else
            {
                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, ShowOnView);
            }
        }
        catch (Exception exception)
        {
            LogLifecycleError(exception, "无法调度应用窗口显示");
        }

        void ShowOnView()
        {
            if (_isExiting || activation.IsClosed)
            {
                return;
            }

            try
            {
                ShowMainPage(window, activation);
            }
            catch (Exception exception)
            {
                LogLifecycleError(exception, "显示应用页面失败");
            }
        }
    }

    private void ShowMainPage(Window window, ViewActivation activation)
    {
        var isLockScreen = activation.Kind is ActivationKind.LockScreen or ActivationKind.LockScreenComponent || LockScreenHosting.IsWindowHostedByLockScreen();
        if (isLockScreen)
        {
            StopEditorWindowProbe(activation);
            WindowTransparency.Enable(window);
        }
        else
        {
            // UWP persists this setting across launches, including builds that used a custom title bar.
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = false;
        }

        // Window.Current belongs to the view activated by the system host.
        if (window.Content is not WindowToastHost toastHost)
        {
            var frame = window.Content as Frame ?? new Frame();
            if (ReferenceEquals(window.Content, frame))
            {
                window.Content = null;
            }

            frame.NavigationFailed += OnNavigationFailed;
            toastHost = new WindowToastHost(window, frame);
            window.Content = toastHost;
        }

        var rootFrame = toastHost.Frame;
        rootFrame.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
        var pageType = isLockScreen ? typeof(MainPage) : typeof(SceneEditorPage);
        // A retained lock view cannot re-enter after its dismissal animation completed.
        var lockScreenRequiresFreshPage = isLockScreen &&
            rootFrame.Content is MainPage { IsDismissalStarted: true };
        var pageChanged = isLockScreen
            ? rootFrame.Content is not MainPage || lockScreenRequiresFreshPage
            : rootFrame.Content is not (SceneEditorPage or ApplicationSettingsPage);
        if (pageChanged)
        {
            toastHost.Clear();
            rootFrame.Navigate(pageType);
            rootFrame.BackStack.Clear();
        }

        if (rootFrame.Content is MainPage mainPage)
        {
            // A retained lock-screen view does not run Loaded again. Refresh it
            // on each activation so edits from another app process are visible.
            if (!pageChanged)
            {
                mainPage.RefreshActiveScene();
            }
        }

        ShowPendingUnhandledException(window);
        window.Activate();
        if (!isLockScreen && !_isExiting && !activation.IsClosed)
        {
            StartEditorWindowProbe(window, activation);
        }
    }

    /// <summary>
        /// Invoked when Navigation to a certain page fails.
        /// </summary>
        /// <param name = "sender">The Frame which failed navigation.</param>
        /// <param name = "e">Details about the navigation failure.</param>
        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new Exception($"Failed to load page '{e.SourcePageType.FullName}'.");
    }

    /// <summary>
        /// Invoked when application execution is being suspended. Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name = "sender">The source of the suspend request.</param>
        /// <param name = "e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
    {
        SuspendingDeferral deferral = e.SuspendingOperation.GetDeferral();
        try
        {
            // Suspending has a short system deadline; do not wait for hosted
            // startup work. Settings are loaded before the host is started.
            if (!_isExiting)
            {
                SaveSettingsSafely("应用挂起");
            }
        }
        catch (Exception exception)
        {
            LogLifecycleError(exception, "挂起时保存应用状态失败");
        }
        finally
        {
            deferral.Complete();
        }
    }
}
