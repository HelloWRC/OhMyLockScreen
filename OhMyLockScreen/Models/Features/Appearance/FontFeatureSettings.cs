using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Attributes.Serialization;

namespace OhMyLockScreen.Models.Features.Appearance;

[AutoJsonTypeName("omls.features.appearance.font")]
public partial class FontFeatureSettings : ObservableObject, IFeatureSettings
{
    [ObservableProperty]
    private string _fontFamilyName = "";

    [ObservableProperty]
    private ushort _fontWeight = 400;
}
