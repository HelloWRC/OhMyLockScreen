using System.Collections.ObjectModel;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Controls.Editor;

public sealed partial class GridDefinitionsControl : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(string), typeof(GridDefinitionsControl), new PropertyMetadata(""));
    public static readonly DependencyProperty DefinitionsProperty = DependencyProperty.Register(nameof(Definitions), typeof(ObservableCollection<GridElement>), typeof(GridDefinitionsControl), new PropertyMetadata(null));
    public string Header
    {
        get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value);
    }
    public ObservableCollection<GridElement>? Definitions
    {
        get => (ObservableCollection<GridElement>?)GetValue(DefinitionsProperty); set => SetValue(DefinitionsProperty, value);
    }

    public GridDefinitionsControl() => InitializeComponent();
    private void OnAddClick(object sender, RoutedEventArgs args) => Definitions?.Add(new GridElement());
    private void OnRemoveClick(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: GridElement definition })
        {
            Definitions?.Remove(definition);
        }
    }
}