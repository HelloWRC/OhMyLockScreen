using System.Diagnostics;
using OhMyLockScreen.Controls;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace OhMyLockScreen.Views;

/// <summary>全屏锁屏场景预览页，复用锁屏手势、键盘兜底与解锁退出动画。</summary>
public sealed partial class ScenePreviewPage : Page
{
    private readonly ISceneService? _scenes;
    private readonly LockScreenDismissalController _dismissal;
    private SystemNavigationManager? _systemNavigation;
    private Guid _sceneId;
    private bool _isLoaded;
    private bool _enteredFullScreen;
    private bool _exited;

    public ScenePreviewPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        RootScenePresenter.ParallaxInputSurface = this;
        RootScenePresenter.ShowSystemBackgroundPreview = true;
        _scenes = IAppHost.TryGetService<ISceneService>();
        _dismissal = new LockScreenDismissalController(
            this,
            RootScenePresenter,
            onCompleteDismissalAsync: CompletePreviewDismissalAsync);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (args.Parameter is Guid id && id != Guid.Empty)
        {
            _sceneId = id;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded || _dismissal.IsDismissalStarted)
        {
            return;
        }

        _isLoaded = true;
        _exited = false;

        if (Window.Current.Content is WindowToastHost toastHost)
        {
            toastHost.Clear();
        }

        try
        {
            if (_scenes != null)
            {
                var targetId = _sceneId != Guid.Empty ? _sceneId : _scenes.ActiveSceneId;
                if (targetId != Guid.Empty)
                {
                    RootScenePresenter.Scene = _scenes.Load(targetId);
                }
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法加载预览场景：{exception}");
        }

        var view = ApplicationView.GetForCurrentView();
        _enteredFullScreen = !view.IsFullScreenMode && view.TryEnterFullScreenMode();

        AttachNavigation();
        _dismissal.Attach(Window.Current.CoreWindow.Dispatcher);
        Focus(FocusState.Programmatic);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        DetachNavigation();
        ExitFullScreenIfEntered();
        _dismissal.Detach();
        RootScenePresenter.ResetUnlockDrag();
    }

    private void AttachNavigation()
    {
        if (_systemNavigation != null)
        {
            return;
        }

        _systemNavigation = SystemNavigationManager.GetForCurrentView();
        _systemNavigation.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
        _systemNavigation.BackRequested += OnBackRequested;
    }

    private void DetachNavigation()
    {
        if (_systemNavigation == null)
        {
            return;
        }

        _systemNavigation.BackRequested -= OnBackRequested;
        _systemNavigation = null;
    }

    private void OnBackRequested(object? sender, BackRequestedEventArgs args)
    {
        if (!args.Handled)
        {
            args.Handled = true;
            ExitPreview();
        }
    }

    private Task CompletePreviewDismissalAsync()
    {
        ExitPreview();
        return Task.CompletedTask;
    }

    private void ExitPreview()
    {
        if (_exited)
        {
            return;
        }

        _exited = true;
        DetachNavigation();
        ExitFullScreenIfEntered();

        if (Frame is { } frame && ReferenceEquals(frame.Content, this))
        {
            if (frame.CanGoBack)
            {
                frame.GoBack();
            }
            else
            {
                frame.Navigate(typeof(SceneManagerPage));
            }
        }
    }

    private void ExitFullScreenIfEntered()
    {
        if (_enteredFullScreen)
        {
            _enteredFullScreen = false;
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
        }
    }
}
