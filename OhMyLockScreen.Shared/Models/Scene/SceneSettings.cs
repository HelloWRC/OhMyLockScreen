using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;

namespace OhMyLockScreen.Shared.Models.Scene;

public partial class SceneSettings : ObservableObject, IFeatureContainer
{
    [ObservableProperty] private ObservableCollection<FeatureInfo> _features = [];

    [ObservableProperty] private ObservableCollection<LayerSettings> _layers = [];
}