using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;

namespace OhMyLockScreen.Shared.Models.Scene;

public partial class ComponentSettings : SettingsWithClientSettingsBase<IComponentSettings>, IFeatureContainer
{
    [ObservableProperty]
    private string _name = "";
    /// <summary>
        /// 组件 Id
        /// </summary>
        [ObservableProperty]
    private string _id = "";
    [ObservableProperty]
    private ObservableCollection<FeatureInfo> _features = [];
}