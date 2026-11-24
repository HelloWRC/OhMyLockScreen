using CommunityToolkit.Mvvm.ComponentModel;

namespace OhMyLockScreen.ViewModels;

public sealed partial class SceneCardItem(Guid id) : ObservableObject
{
    public Guid Id { get; } = id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveAutomationName))]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string _description = "";

    [ObservableProperty]
    private string _author = "";

    [ObservableProperty]
    private string _license = "";

    [ObservableProperty]
    private string? _previewImageBlobId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewImage))]
    private Uri? _previewImageUri;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete), nameof(CanSetActive), nameof(ActiveAutomationName))]
    private bool _isActive;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasPreviewImage => PreviewImageUri != null;
    public bool CanDelete => !IsActive;
    public bool CanSetActive => !IsActive;
    public string ActiveAutomationName => IsActive ? $"{Name}（当前锁屏）" : Name;
}
