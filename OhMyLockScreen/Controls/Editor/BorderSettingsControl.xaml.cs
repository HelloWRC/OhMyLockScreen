using OhMyLockScreen.ViewModels;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Controls.Editor;

public sealed partial class BorderSettingsControl : BorderSettingsControlBase
{
    private bool _loaded;

    public BorderSettingsViewModel? Values { get; private set; }

    public BorderSettingsControl() => InitializeComponent();

    protected override void OnSettingsChanged()
    {
        Values?.Dispose();
        Values = SettingsInternal == null ? null : new BorderSettingsViewModel(Settings);
        if (_loaded)
        {
            Values?.Attach();
        }

        OnPropertyChanged(nameof(Values));
        Bindings.Update();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        Values?.Attach();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        Values?.Dispose();
    }
}
