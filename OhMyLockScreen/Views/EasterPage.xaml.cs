using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace OhMyLockScreen.Views;

public sealed partial class EasterPage : Page
{
    public EasterPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        var navigation = SystemNavigationManager.GetForCurrentView();
        navigation.AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
        navigation.BackRequested += OnBackRequested;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        SystemNavigationManager.GetForCurrentView().BackRequested -= OnBackRequested;
        base.OnNavigatedFrom(args);
    }

    private void OnBackRequested(object? sender, BackRequestedEventArgs args)
    {
        if (!args.Handled && Frame.CanGoBack && ReferenceEquals(Frame.Content, this))
        {
            args.Handled = true;
            Frame.GoBack();
        }
    }
}
