using System;
using Windows.ApplicationModel;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen;
/// <summary>
/// Provides application-specific behavior to supplement the default <see cref = "Application"/> class.
/// </summary>
public sealed partial class App : Application
{
    private readonly ConditionalWeakTable<Window, ViewActivation> _viewActivations = new();
    private sealed class ViewActivation
    {
        internal ActivationKind? Kind
        {
            get; set;
        }
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
        _initializeHost = new Lazy<Task>(() => StartHostAsync(hostFactory));
        InitializeComponent();
        Suspending += OnSuspending;
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
        // Keep the latest OnActivated value per view, including during host startup.
        var activation = _viewActivations.GetValue(window, _ => new ViewActivation());
        activation.Kind = activationKind;
        try
        {
            await _initializeHost.Value;
        }
        catch (Exception exception)
        {
            LogLifecycleError(exception, "应用主机启动失败");
            return;
        }

        if (!_isExiting)
        {
            ShowMainPage(window, activation.Kind);
        }
    }

    private void ShowMainPage(Window window, ActivationKind? activationKind)
    {
        var isLockScreen = activationKind is ActivationKind.LockScreen or ActivationKind.LockScreenComponent || LockScreenHosting.IsWindowHostedByLockScreen();
        if (isLockScreen)
        {
            WindowTransparency.Enable(window);
        }

        // Window.Current belongs to the view activated by the system host.
        if (window.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            window.Content = rootFrame;
        }

        rootFrame.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
        var pageType = isLockScreen ? typeof(MainPage) : typeof(SceneEditorPage);
        var pageChanged = rootFrame.Content?.GetType() != pageType;
        if (pageChanged)
        {
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

        window.Activate();
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
