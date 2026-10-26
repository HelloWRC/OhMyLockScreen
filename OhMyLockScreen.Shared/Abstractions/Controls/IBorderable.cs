using System.ComponentModel;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Abstractions.Controls;

/// <summary>公开当前的装饰边框，并在其加载、替换或卸载时通知订阅者。</summary>
public interface IBorderable : INotifyPropertyChanging, INotifyPropertyChanged
{
    Border? Border { get; }
}
