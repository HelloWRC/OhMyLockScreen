using System.Collections.ObjectModel;
using System.ComponentModel;
using OhMyLockScreen.Shared.Models.FeatureHost;

namespace OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;

public interface IFeatureContainer : INotifyPropertyChanged, INotifyPropertyChanging
{
    /// <summary>
    /// 此对象包含的特性
    /// </summary>
    public ObservableCollection<FeatureInfo> Features { get; set; }
}