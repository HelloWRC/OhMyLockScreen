using System.Collections.ObjectModel;
using Windows.UI.Xaml.Controls;
using OhMyLockScreen.Shared.Models.Scene;

namespace OhMyLockScreen.Shared.Abstractions.Models.Scene;

public interface ILayoutSettings
{
    public LayoutKind Kind { get; set; }

    public double Spacing { get; set; }

    public ObservableCollection<GridElement> Rows { get; }

    public ObservableCollection<GridElement> Columns { get; }

    public Orientation Orientation { get; set; }
}

public enum LayoutKind
{
    Canvas,
    StackPanel,
    WrapPanel,
    Grid,
}
