using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

namespace OhMyLockScreen;

/// <summary>Lock screen clock and animated dismissal to the system sign-in screen.</summary>
public sealed partial class MainPage : Page
{
    private static readonly CultureInfo DateCulture = CultureInfo.GetCultureInfo("zh-CN");
    private readonly TimeProvider timeProvider;
    private readonly DispatcherTimer clockTimer;
    private Window? viewWindow;
    private Storyboard? dismissStoryboard;
    private bool isPageLoaded;
    private bool isDismissing;
    private bool hasExited;

    internal void ShowActivationKind(ActivationKind kind)
    {
    }

    public MainPage() : this(TimeProvider.System)
    {
    }

    internal MainPage(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
        InitializeComponent();
        RootScenePresenter.Scene = new SceneSettings()
        {
            Layers =
            {
                new LayerSettings()
                {
                    Kind = LayoutKind.Grid,
                    Children =
                    {
                        new ComponentSettings()
                        {
                            Id = "omls.components.text",
                            Features =
                            {
                                new FeatureInfo()
                                {
                                    Id = "omls.features.layout.layout",
                                    Settings = new LayoutFeatureSettings()
                                    {
                                        HorizontalAlignment = HorizontalAlignment.Center,
                                        VerticalAlignment = VerticalAlignment.Top,
                                        Margin = new Thickness(0, 32, 0, 0)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        //ConfigureFileHelper.SaveConfig(Path.Combine(ApplicationData.Current.LocalFolder.Path, "test.json"), RootScenePresenter.Scene);
        clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (isPageLoaded || isDismissing)
        {
            return;
        }

        isPageLoaded = true;
        viewWindow = Window.Current;
        viewWindow.CoreWindow.PointerPressed += OnPointerPressed;
        viewWindow.CoreWindow.KeyDown += OnKeyDown;
        // Alt and other system keys may be delivered as accelerator events.
        viewWindow.CoreWindow.Dispatcher.AcceleratorKeyActivated += OnAcceleratorKeyActivated;

        clockTimer.Start();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        isPageLoaded = false;
        DetachViewEvents();
        if (isDismissing)
        {
            // An unloaded view cannot be relied on to finish its storyboard.
            await ExitAsync();
        }
    }

    private void DetachViewEvents()
    {
        clockTimer.Stop();
        if (viewWindow != null)
        {
            viewWindow.CoreWindow.PointerPressed -= OnPointerPressed;
            viewWindow.CoreWindow.KeyDown -= OnKeyDown;
            viewWindow.CoreWindow.Dispatcher.AcceleratorKeyActivated -= OnAcceleratorKeyActivated;
            viewWindow = null;
        }
    }

    private void OnPointerPressed(CoreWindow sender, PointerEventArgs e)
    {
        e.Handled = true;
        RequestDismissal();
    }

    private void OnKeyDown(CoreWindow sender, KeyEventArgs e)
    {
        e.Handled = true;
        RequestDismissal();
    }

    private void OnAcceleratorKeyActivated(CoreDispatcher sender, AcceleratorKeyEventArgs e)
    {
        if (e.EventType is CoreAcceleratorKeyEventType.KeyDown or CoreAcceleratorKeyEventType.SystemKeyDown)
        {
            e.Handled = true;
            RequestDismissal();
        }
    }

    private void RequestDismissal()
    {
        BeginDismissal();
    }

    private async void BeginDismissal()
    {
        if (!isPageLoaded || isDismissing)
        {
            return;
        }

        isDismissing = true;
        DetachViewEvents();
        try
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法启动锁屏退出动画: {exception}");
            await ExitAsync();
        }
    }

    private async void OnDismissCompleted(object? sender, object e) => await ExitAsync();

    private async Task ExitAsync()
    {
        if (hasExited)
        {
            return;
        }

        hasExited = true;
        DetachViewEvents();

        if (Application.Current is App app)
        {
            await app.ExitAsync();
        }
        else
        {
            CoreApplication.Exit();
        }
    }
}
