using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Services.LockScreenProvider;
using OhMyLockScreen.Shared;
using System.Text.Json.Serialization;

namespace OhMyLockScreen.Models;
/// <summary>应用设置。在此添加使用 SetProperty 通知变更的持久化属性。</summary>
public partial class Settings : ObservableObject
{
    [ObservableProperty]
    private Guid? _activeSceneId;
    [ObservableProperty]
    private Guid? _lastEditedSceneId;
    [ObservableProperty]
    private bool _compatibilityUnlockMode;

    [JsonIgnore]
    public bool ReplaceSystemLockScreen
    {
        get
        {
            var service = IAppHost.TryGetService<LockScreenReplacementService>();
            if (service == null)
            {
                return false;
            }

            service.EnsureInitialized();
            return service.CurrentValue;
        }
        set
        {
            var service = IAppHost.TryGetService<LockScreenReplacementService>();
            if (service == null)
            {
                return;
            }

            if (service.IsInitialized && service.CurrentValue == value)
            {
                OnPropertyChanged(nameof(ReplaceSystemLockScreen));
                return;
            }

            service.SetEnabled(value);
            OnPropertyChanged(nameof(ReplaceSystemLockScreen));
        }
    }

    internal void NotifyReplaceSystemLockScreenChanged() =>
        OnPropertyChanged(nameof(ReplaceSystemLockScreen));
}
