using CommunityToolkit.Mvvm.ComponentModel;

namespace OhMyLockScreen.ViewModels;

public sealed partial class SceneListItem(Guid id) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnableLabel))]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    private bool _isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    private bool _isPointerOver;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    private bool _hasFocus;

    public Guid Id { get; } = id;
    public string EnableLabel => $"将“{Name}”设为锁屏场景";
    public bool CanDelete => !IsActive;
    public bool ShowActions => IsEditing || IsPointerOver || HasFocus;
}
