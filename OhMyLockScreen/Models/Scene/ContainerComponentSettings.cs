using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Serialization;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Models.Scene;

[AutoJsonTypeName("omls.container")]
public partial class ContainerComponentSettings : ObservableObject, IComponentContainerSettings, ILayoutSettings
{
    [ObservableProperty]
    private ObservableCollection<ComponentSettings> _children = [];
    [ObservableProperty]
    private LayoutKind _kind = LayoutKind.StackPanel;
    [ObservableProperty]
    private double _spacing = 10;
    [ObservableProperty]
    private Orientation _orientation = Orientation.Vertical;
    [ObservableProperty]
    private ObservableCollection<GridElement> _rows = [];
    [ObservableProperty]
    private ObservableCollection<GridElement> _columns = [];
}