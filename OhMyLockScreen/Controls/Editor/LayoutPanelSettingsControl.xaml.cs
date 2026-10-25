using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Controls.Editor;

public sealed partial class LayoutPanelSettingsControl : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model),
        typeof(ILayoutSettings),
        typeof(LayoutPanelSettingsControl),
        new PropertyMetadata(null, OnModelChanged));
    public LayoutPanelSettingsViewModel ViewModel
    {
        get;
    } = new();
    public ILayoutSettings? Model
    {
        get => (ILayoutSettings?)GetValue(ModelProperty); set => SetValue(ModelProperty, value);
    }

    public LayoutPanelSettingsControl() => InitializeComponent();

    private static void OnModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (LayoutPanelSettingsControl)sender;
        control.ViewModel.Model = (ILayoutSettings?)args.NewValue;
        control.Bindings.Update();
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => ViewModel.Attach();
    private void OnUnloaded(object sender, RoutedEventArgs args) => ViewModel.Dispose();
}
