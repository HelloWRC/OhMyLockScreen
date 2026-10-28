using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Serialization;
using OhMyLockScreen.Shared.Models.SystemStatus;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Models.Scene;

[AutoJsonTypeName("omls.systemStatus")]
[Bindable]
public partial class SystemStatusComponentSettings : ObservableObject, IComponentSettings
{
    [ObservableProperty] private SystemStatusKind _kind = SystemStatusKind.Battery;
    [ObservableProperty] private bool _showIcon = true;
    [ObservableProperty] private bool _showText;
    [ObservableProperty] private double _iconSize = 24;
}
