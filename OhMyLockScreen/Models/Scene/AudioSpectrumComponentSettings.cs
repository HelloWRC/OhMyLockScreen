using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Serialization;
using OhMyLockScreen.Shared.Models.Audio;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Models.Scene;

[AutoJsonTypeName("omls.audioSpectrum")]
[Bindable]
public partial class AudioSpectrumComponentSettings : ObservableObject, IComponentSettings
{
    [ObservableProperty] private SpectrumDock _dock = SpectrumDock.Bottom;
    [ObservableProperty] private SpectrumColorSource _colorSource = SpectrumColorSource.MediaCover;
    [ObservableProperty] private int _barCount = 64;
    [ObservableProperty] private double _gap = 2;
    [ObservableProperty] private double _sensitivity = 1;
}
