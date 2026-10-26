using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Models.Features.Appearance;

public partial class ForegroundFeatureSettings : ObservableObject, IFeatureSettings
{
    [ObservableProperty]
    private Brush? _foreground;
}
