using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Serialization;

namespace OhMyLockScreen.Models.Scene;

[AutoJsonTypeName("omls.clock")]
public partial class ClockComponentSettings : ObservableObject, IComponentSettings
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMinuteSeparator), nameof(ShowSecondSeparator))]
    private bool _showHours = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMinuteSeparator), nameof(ShowSecondSeparator))]
    private bool _showMinutes = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSecondSeparator))]
    private bool _showSeconds;
    [ObservableProperty]
    private bool _flashSeparator;

    [JsonIgnore]
    public bool ShowMinuteSeparator => ShowHours && ShowMinutes;

    [JsonIgnore]
    public bool ShowSecondSeparator => (ShowHours || ShowMinutes) && ShowSeconds;
}
