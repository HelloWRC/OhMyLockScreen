using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Models.Features.Appearance;

public partial class ThemeFeatureSettings : ObservableObject, IFeatureSettings
{
    [ObservableProperty]
    private ElementTheme _requestedTheme = ElementTheme.Default;
}
