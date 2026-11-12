using System.Diagnostics;
using FluentIcons.Common;
using OhMyLockScreen.Models;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.LockScreenProvider;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Attributes;
using OhMyLockScreen.Shared.Enums.SettingsPages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Views.SettingsPages;

[SettingsPageInfo("general", "通用", Icon.Settings, SettingsPageCategory.Internal)]
public sealed partial class GeneralSettingsPage : SettingsPageBase
{
    private readonly LockScreenReplacementService _lockScreenReplacement;
    private bool _isUpdatingLockScreenToggle;

    public Settings Settings { get; }

    public GeneralSettingsPage()
    {
        Settings = IAppHost.GetRequiredService<SettingsService>().Settings;
        _lockScreenReplacement = IAppHost.GetRequiredService<LockScreenReplacementService>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args) =>
        await _lockScreenReplacement.EnsureInitializedAsync();

    private async void OnReplaceSystemLockScreenToggled(object sender, RoutedEventArgs args)
    {
        if (_isUpdatingLockScreenToggle || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var requestedValue = toggle.IsOn;
        var currentValue = Settings.ReplaceSystemLockScreen;
        if (requestedValue == currentValue)
        {
            return;
        }

        if (!requestedValue)
        {
            _isUpdatingLockScreenToggle = true;
            try
            {
                toggle.IsOn = true;
            }
            finally
            {
                _isUpdatingLockScreenToggle = false;
            }

            if (!await ConfirmRestartExplorerAsync())
            {
                return;
            }
        }

        _isUpdatingLockScreenToggle = true;
        try
        {
            toggle.IsOn = requestedValue;
            Settings.ReplaceSystemLockScreen = requestedValue;
        }
        finally
        {
            _isUpdatingLockScreenToggle = false;
        }
    }

    private async Task<bool> ConfirmRestartExplorerAsync()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            RestartExplorerDialog.XamlRoot = XamlRoot;
        }

        try
        {
            return await RestartExplorerDialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法显示恢复默认锁屏确认框：{exception}");
            return false;
        }
    }
}