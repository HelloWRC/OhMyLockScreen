using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;

namespace OhMyLockScreen.Shared.Models.FeatureHost;

public partial class FeatureInfo : SettingsWithClientSettingsBase<IFeatureSettings>
{
    [ObservableProperty] private string _id = "";
}