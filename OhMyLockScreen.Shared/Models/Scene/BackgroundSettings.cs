using CommunityToolkit.Mvvm.ComponentModel;

namespace OhMyLockScreen.Shared.Models.Scene;

public partial class BackgroundSettings : ObservableObject
{
    [ObservableProperty]
    private BackgroundMode _mode;
    [ObservableProperty]
    private string _source = "";
    [ObservableProperty]
    private bool _enableSound;
}

public enum BackgroundMode
{
    None,
    Video
}