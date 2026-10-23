using CommunityToolkit.Mvvm.ComponentModel;

namespace OhMyLockScreen.Shared.Models.Scene;

public partial class ComponentSettings : ObservableObject
{
    /// <summary>
    /// 组件 Id
    /// </summary>
    [ObservableProperty] private string _id = "";
    /// <summary>
    /// 组件设置（如有）
    /// </summary>
    [ObservableProperty] private object? _settings;
    
}