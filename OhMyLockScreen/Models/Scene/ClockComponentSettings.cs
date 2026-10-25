using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Serialization;

namespace OhMyLockScreen.Models.Scene;

[AutoJsonTypeName("omls.clock")]
public partial class ClockComponentSettings : ObservableObject, IComponentSettings
{
    [ObservableProperty]
    private bool _showSeconds;
    [ObservableProperty]
    private bool _flashSeparator;
    [ObservableProperty]
    private double _fontSize = 64;
}
