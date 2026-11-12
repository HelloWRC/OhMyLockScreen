using System.Diagnostics;
using FluentIcons.Common;
using FluentIcons.Uwp;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.LockScreenProvider;
using OhMyLockScreen.Shared.Models.LockScreenProvider;
using OhMyLockScreen.Shared.Services.Registry;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Muxc = Microsoft.UI.Xaml.Controls;

namespace OhMyLockScreen.Views;

public sealed partial class ApplicationSettingsPage : Page
{
    private readonly SettingsPageRegistry _registry;
    private readonly SettingsService _settingsService;
    private readonly LockScreenReplacementService _lockScreenReplacement;
    private readonly Dictionary<string, Muxc.NavigationViewItem> _menuItems = new(StringComparer.Ordinal);
    private bool _keepHistory;
    private Window? _window;
    private Frame? _rootFrame;
    private SystemNavigationManager? _systemNavigation;
    private long _rootBackToken;
    private long _settingsBackToken;
    private bool _lockScreenEventsAttached;

    public ApplicationSettingsPage()
    {
        _registry = IAppHost.GetRequiredService<SettingsPageRegistry>();
        _settingsService = IAppHost.GetRequiredService<SettingsService>();
        _lockScreenReplacement = IAppHost.GetRequiredService<LockScreenReplacementService>();
        InitializeComponent();

        SettingsFrame.Tag = (Func<string, bool, bool>)NavigateToSettingsPage;
        SettingsFrame.Navigated += OnSettingsNavigated;
        SettingsFrame.NavigationFailed += OnSettingsNavigationFailed;
        SettingsNavigation.ItemInvoked += OnSettingsItemInvoked;
        SettingsNavigation.DisplayModeChanged += (_, args) => ApplyTitleInsets(args.DisplayMode);
        PopulateMenu();
        ApplyTitleInsets(SettingsNavigation.DisplayMode);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void PopulateMenu()
    {
        var iconStyle = (Style)Resources["SettingsNavigationIconStyle"];
        foreach (var group in _registry.Pages.GroupBy(page => page.Info.Category))
        {
            if (SettingsNavigation.MenuItems.Count > 0)
            {
                SettingsNavigation.MenuItems.Add(new Muxc.NavigationViewItemSeparator());
            }

            foreach (var page in group)
            {
                var item = new Muxc.NavigationViewItem
                {
                    Content = page.Info.Name,
                    Tag = page.Info.Id,
                    Icon = new FluentIcon
                    {
                        Style = iconStyle,
                        IconSize = IconSize.Resizable,
                        Icon = page.Info.Icon
                    }
                };
                _menuItems.Add(page.Info.Id, item);
                SettingsNavigation.MenuItems.Add(item);
            }
        }
    }

    private void ApplyTitleInsets(Muxc.NavigationViewDisplayMode mode)
    {
        var expanded = mode == Muxc.NavigationViewDisplayMode.Expanded;
        TitleArea.Margin = (Thickness)Resources[expanded ? "SettingsExpandedTitleMargin" : "SettingsCompactTitleMargin"];
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        AttachSystemNavigation();
        AttachLockScreenReplacementEvents();
        if (SettingsFrame.Content == null)
        {
            NavigateToSettingsPage("general");
        }
    }

    private void OnSettingsItemInvoked(Muxc.NavigationView sender, Muxc.NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string id)
        {
            NavigateToSettingsPage(id);
        }
    }

    internal bool NavigateToSettingsPage(string id, bool keepHistory = false)
    {
        if (_registry.Find(id) is not { } registration)
        {
            return false;
        }

        var previousKeepHistory = _keepHistory;
        _keepHistory = keepHistory;
        try
        {
            return SettingsFrame.Navigate(registration.PageType);
        }
        finally
        {
            _keepHistory = previousKeepHistory;
        }
    }

    private void OnSettingsNavigated(object sender, NavigationEventArgs args)
    {
        if (args.Content is SettingsPageBase page)
        {
            page.NavigationCacheMode = NavigationCacheMode.Disabled;
        }

        if (args.NavigationMode == NavigationMode.New && !_keepHistory)
        {
            SettingsFrame.BackStack.Clear();
            SettingsFrame.ForwardStack.Clear();
        }

        UpdatePagePresentation(args.SourcePageType);
        UpdateSystemBackButton();
    }

    private void OnSettingsNavigationFailed(object sender, NavigationFailedEventArgs args)
    {
        args.Handled = true;
        UpdatePagePresentation(SettingsFrame.Content?.GetType());
        this.ShowErrorToast("无法打开设置页", args.Exception);
    }

    private void UpdatePagePresentation(Type? pageType)
    {
        var info = pageType == null ? null : _registry.Find(pageType)?.Info;
        SettingsNavigation.SelectedItem = info == null ? null : _menuItems[info.Id];
        SettingsTitle.Text = info?.Name ?? string.Empty;
        TitleArea.Visibility = info is { HidePageTitle: false } ? Visibility.Visible : Visibility.Collapsed;
        if (info is { UseFullWidth: true })
        {
            TitleArea.MaxWidth = double.PositiveInfinity;
        }
        else
        {
            TitleArea.ClearValue(FrameworkElement.MaxWidthProperty);
        }
    }

    private void AttachLockScreenReplacementEvents()
    {
        if (_lockScreenEventsAttached)
        {
            return;
        }

        _lockScreenEventsAttached = true;
        _lockScreenReplacement.StateChanged += OnLockScreenReplacementStateChanged;
        _lockScreenReplacement.OperationFailed += OnLockScreenReplacementFailed;
    }

    private void DetachLockScreenReplacementEvents()
    {
        if (!_lockScreenEventsAttached)
        {
            return;
        }

        _lockScreenEventsAttached = false;
        _lockScreenReplacement.StateChanged -= OnLockScreenReplacementStateChanged;
        _lockScreenReplacement.OperationFailed -= OnLockScreenReplacementFailed;
    }

    private async void OnLockScreenReplacementStateChanged(
        object? sender,
        LockScreenReplacementStateChangedEventArgs args)
    {
        var window = _window;
        if (window == null)
        {
            return;
        }

        try
        {
            await window.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () => _settingsService.Settings.NotifyReplaceSystemLockScreenChanged());
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法通知锁屏替换状态：{exception}");
        }
    }

    private async void OnLockScreenReplacementFailed(
        object? sender,
        LockScreenReplacementFailedEventArgs args)
    {
        var window = _window;
        if (window == null)
        {
            return;
        }

        var title = args.Operation == LockScreenProviderOperation.Query
            ? "无法读取系统锁屏设置"
            : "无法更改系统锁屏设置";
        try
        {
            await window.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () => this.ShowErrorToast(title, args.Exception));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法显示锁屏替换错误：{exception}");
        }
    }

    private void AttachSystemNavigation()
    {
        if (_systemNavigation != null || Frame is not { } root)
        {
            return;
        }

        _rootFrame = root;
        _window = Window.Current;
        _window.Closed += OnWindowClosed;
        _systemNavigation = SystemNavigationManager.GetForCurrentView();
        _systemNavigation.BackRequested += OnSystemBackRequested;
        _rootBackToken = root.RegisterPropertyChangedCallback(Frame.CanGoBackProperty, OnCanGoBackChanged);
        _settingsBackToken = SettingsFrame.RegisterPropertyChangedCallback(Frame.CanGoBackProperty, OnCanGoBackChanged);
        UpdateSystemBackButton();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        DetachLockScreenReplacementEvents();
        DetachSystemNavigation();
    }

    private void OnWindowClosed(object sender, CoreWindowEventArgs args)
    {
        DetachLockScreenReplacementEvents();
        DetachSystemNavigation();
    }

    private void DetachSystemNavigation()
    {
        if (_systemNavigation is not { } systemNavigation || _rootFrame is not { } root)
        {
            return;
        }

        _window!.Closed -= OnWindowClosed;
        systemNavigation.BackRequested -= OnSystemBackRequested;
        root.UnregisterPropertyChangedCallback(Frame.CanGoBackProperty, _rootBackToken);
        SettingsFrame.UnregisterPropertyChangedCallback(Frame.CanGoBackProperty, _settingsBackToken);
        systemNavigation.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;
        _systemNavigation = null;
        _rootFrame = null;
        _window = null;
    }

    private void OnCanGoBackChanged(DependencyObject sender, DependencyProperty property) => UpdateSystemBackButton();

    private void UpdateSystemBackButton()
    {
        if (_systemNavigation is { } systemNavigation)
        {
            systemNavigation.AppViewBackButtonVisibility = SettingsFrame.CanGoBack || _rootFrame?.CanGoBack == true
                ? AppViewBackButtonVisibility.Visible
                : AppViewBackButtonVisibility.Collapsed;
        }
    }

    private void OnSystemBackRequested(object? sender, BackRequestedEventArgs args)
    {
        if (!args.Handled)
        {
            args.Handled = TryGoBack();
        }
    }

    internal bool TryGoBack()
    {
        if (_rootFrame is not { } root || !ReferenceEquals(root.Content, this))
        {
            return false;
        }

        var destination = SettingsFrame.CanGoBack ? SettingsFrame : root;
        if (!destination.CanGoBack)
        {
            return false;
        }

        destination.GoBack();
        return true;
    }
}
