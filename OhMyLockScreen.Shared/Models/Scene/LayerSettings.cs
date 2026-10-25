using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using System.Collections.ObjectModel;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Models.Scene;

public partial class LayerSettings : ObservableObject, IFeatureContainer, ILayoutSettings
{
    [ObservableProperty]
    private string _name = "图层";
    [ObservableProperty]
    private ObservableCollection<FeatureInfo> _features = [];
    [ObservableProperty]
    private ObservableCollection<ComponentSettings> _children = [];
    [ObservableProperty]
    private LayoutKind _kind;
    [ObservableProperty]
    private double _spacing = 8;
    [ObservableProperty]
    private ObservableCollection<GridElement> _rows = [];
    [ObservableProperty]
    private ObservableCollection<GridElement> _columns = [];
    [ObservableProperty]
    private Orientation _orientation;
}