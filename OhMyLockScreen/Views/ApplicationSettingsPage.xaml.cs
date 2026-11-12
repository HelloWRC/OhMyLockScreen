using FluentIcons.Common;
using FluentIcons.Uwp;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Helpers.UI;
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
    private readonly Dictionary<string, Muxc.NavigationViewItem> _menuItems = new(StringComparer.Ordinal);
    private bool _keepHistory;
    private Window? _window;
    private Frame? _rootFrame;
    private SystemNavigationManager? _systemNavigation;
    private long _rootBackToken;
    private long _settingsBackToken;

    public ApplicationSettingsPage()
    {
        _registry = IAppHost.GetRequiredService<SettingsPageRegistry>();
        InitializeComponent();

        SettingsFrame.Tag = (Func<string, bool, bool>)NavigateToSettingsPage;
        SettingsFrame.Navigated += OnSettingsNavigated;
        SettingsFrame.NavigationFailed += OnSettingsNavigationFailed;
        SettingsNavigation.ItemInvoked += OnSettingsItemInvoked;
        SettingsNavigation.DisplayModeChanged += (_, args) => ApplyTitleInsets(args.DisplayMode);
        PopulateMenu();
        ApplyTitleInsets(SettingsNavigation.DisplayMode);

        Loaded += OnLoaded;
        Unloaded += (_, _) => DetachSystemNavigation();
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

    private void OnWindowClosed(object sender, CoreWindowEventArgs args) => DetachSystemNavigation();

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
