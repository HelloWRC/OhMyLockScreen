using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Serialization;
using OhMyLockScreen.Shared.Models.Media;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Models.Scene;

[Bindable]
public abstract partial class MediaSettingsBase : ObservableObject, IComponentSettings
{
    [ObservableProperty] private bool _hideWhenNoMedia = true;
}

[AutoJsonTypeName("omls.media")]
[Bindable]
public partial class MediaComponentSettings : MediaSettingsBase
{
    [ObservableProperty] private MediaVariant _variant = MediaVariant.Lite;
}

[AutoJsonTypeName("omls.mediaText")]
[Bindable]
public partial class MediaTextComponentSettings : MediaSettingsBase
{
    [ObservableProperty] private MediaTextField _field = MediaTextField.Title;
}

[AutoJsonTypeName("omls.mediaController")]
[Bindable]
public partial class MediaControllerComponentSettings : MediaSettingsBase
{
    [ObservableProperty] private MediaAction _action = MediaAction.PlayPause;
}

[AutoJsonTypeName("omls.mediaCover")]
[Bindable]
public partial class MediaCoverComponentSettings : MediaSettingsBase
{
    [ObservableProperty] private bool _showSideDim = true;
}

[AutoJsonTypeName("omls.mediaProgress")]
[Bindable]
public partial class MediaProgressComponentSettings : MediaSettingsBase;
