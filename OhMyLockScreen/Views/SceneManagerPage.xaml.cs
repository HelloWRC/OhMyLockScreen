using Microsoft.UI.Xaml.Controls;
using OhMyLockScreen.Services.LockScreenProvider;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Models.UI;
using OhMyLockScreen.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace OhMyLockScreen.Views;

/// <summary>应用桌面主页：展示、筛选、切换、导入导出及管理锁屏场景。</summary>
public sealed partial class SceneManagerPage : Page
{
    private readonly ISceneService _scenes;
    private readonly LockScreenReplacementService _lockScreenReplacement;
    private readonly HashSet<ToastMessage> _pageToasts = [];
    private Window? _window;
    private bool _loaded;
    private int _lockScreenStatusRequest;
    private bool? _lastLockScreenEnabled;

    public SceneManagerViewModel ViewModel { get; }

    public SceneManagerPage()
    {
        _scenes = IAppHost.GetRequiredService<ISceneService>();
        _lockScreenReplacement = IAppHost.GetRequiredService<LockScreenReplacementService>();
        ViewModel = IAppHost.GetRequiredService<SceneManagerViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _window = Window.Current;
        _lastLockScreenEnabled = null;
        SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;

        _window.Activated += OnWindowActivated;
        _window.CoreWindow.KeyDown += OnKeyDown;
        _scenes.SceneSaved += OnSceneServiceChanged;
        _scenes.ActiveSceneChanged += OnSceneServiceChanged;

        try
        {
            ViewModel.RefreshScenes();
            if (_scenes.LoadErrors.Count > 0)
            {
                ShowMessage("部分场景未能加载", string.Join(Environment.NewLine, _scenes.LoadErrors), InfoBarSeverity.Warning);
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }

        await RefreshLockScreenStatusAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (!_loaded)
        {
            return;
        }

        _loaded = false;
        ClearPageToasts();
        _scenes.SceneSaved -= OnSceneServiceChanged;
        _scenes.ActiveSceneChanged -= OnSceneServiceChanged;
        if (_window != null)
        {
            _window.Activated -= OnWindowActivated;
            _window.CoreWindow.KeyDown -= OnKeyDown;
            _window = null;
        }
    }

    private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != CoreWindowActivationState.Deactivated)
        {
            await RefreshLockScreenStatusAsync();
        }
    }

    private void OnKeyDown(CoreWindow sender, KeyEventArgs args)
    {
        if (_loaded && args.VirtualKey == VirtualKey.F5)
        {
            args.Handled = true;
            ReloadScenes();
        }
    }

    private async Task RefreshLockScreenStatusAsync()
    {
        var request = ++_lockScreenStatusRequest;
        try
        {
            var isEnabled = await _lockScreenReplacement.IsEnabledAsync();
            await OnViewAsync(() =>
            {
                if (request == _lockScreenStatusRequest && _lastLockScreenEnabled != isEnabled)
                {
                    _lastLockScreenEnabled = isEnabled;
                    LockScreenReplacementInfoBar.IsOpen = !isEnabled;
                }
            });
        }
        catch (Exception exception)
        {
            await OnViewAsync(() =>
            {
                if (request == _lockScreenStatusRequest)
                {
                    ShowMessage("无法读取系统锁屏设置", exception.Message);
                }
            });
        }
    }

    private async void OnSceneServiceChanged(object? sender, SceneChangedEventArgs args)
    {
        await OnViewAsync(() =>
        {
            try
            {
                ViewModel.RefreshScenes();
            }
            catch (Exception exception)
            {
                ShowError(exception);
            }
        });
    }

    private async Task OnViewAsync(Action action)
    {
        var window = _window;
        if (!_loaded || window == null)
        {
            return;
        }

        try
        {
            if (window.CoreWindow.Dispatcher.HasThreadAccess)
            {
                action();
            }
            else
            {
                await window.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (_loaded && ReferenceEquals(_window, window))
                    {
                        action();
                    }
                });
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"无法刷新场景管理器：{exception}");
        }
    }

    private void ReloadScenes() => TryAction(() =>
    {
        _scenes.Reload();
        ViewModel.RefreshScenes();
        if (_scenes.LoadErrors.Count > 0)
        {
            ShowMessage("部分场景未能加载", string.Join(Environment.NewLine, _scenes.LoadErrors), InfoBarSeverity.Warning);
        }
    });

    private async void OnCreateSceneClick(object sender, RoutedEventArgs args)
    {
        try
        {
            if (await AskNameAsync("新建场景", "新场景") is { } name && _loaded)
            {
                var created = ViewModel.CreateScene(name);
                Frame.Navigate(typeof(SceneEditorPage), created.SceneId);
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void OnImportSceneClick(object sender, RoutedEventArgs args)
    {
        if (ViewModel.IsBusy)
        {
            return;
        }

        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".omls");

            var file = await picker.PickSingleFileAsync();
            if (file == null || !_loaded)
            {
                return;
            }

            ViewModel.IsBusy = true;
            try
            {
                await using var stream = await file.OpenStreamForReadAsync();
                var imported = await ViewModel.ImportSceneAsync(stream);
                if (_loaded)
                {
                    TrackToast(this.ShowSuccessToastRef($"已导入场景：{imported.Name}"));
                }
            }
            finally
            {
                ViewModel.IsBusy = false;
            }
        }
        catch (Exception exception)
        {
            if (_loaded)
            {
                ShowError(exception);
            }
        }
    }

    private async void OnOpenScenesFolderClick(object sender, RoutedEventArgs args)
    {
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(_scenes.ScenesPath);
            if (!await Launcher.LaunchFolderAsync(folder))
            {
                ShowMessage("无法打开场景文件夹", folder.Path);
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs args) =>
        TryAction(() => Frame.Navigate(typeof(ApplicationSettingsPage)));

    private void OnCardActivated(object? sender, SceneCardItem item) =>
        TryAction(() => ViewModel.SetActive(item.Id));

    private void OnCardEditRequested(object? sender, SceneCardItem item) =>
        TryAction(() => Frame.Navigate(typeof(SceneEditorPage), item.Id));

    private void OnCardPreviewRequested(object? sender, SceneCardItem item) =>
        TryAction(() => Frame.Navigate(typeof(ScenePreviewPage), item.Id));

    private async void OnCardExportRequested(object? sender, SceneCardItem item)
    {
        if (ViewModel.IsBusy)
        {
            return;
        }

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = SanitizeFileName(item.Name)
            };
            picker.FileTypeChoices.Add("OhMyLockScreen 场景包", [".omls"]);
            picker.DefaultFileExtension = ".omls";

            var file = await picker.PickSaveFileAsync();
            if (file == null || !_loaded)
            {
                return;
            }

            ViewModel.IsBusy = true;
            try
            {
                CachedFileManager.DeferUpdates(file);
                await using (var stream = await file.OpenStreamForWriteAsync())
                {
                    if (stream.CanSeek)
                    {
                        stream.SetLength(0);
                    }

                    await ViewModel.ExportSceneAsync(item.Id, stream);
                    await stream.FlushAsync();
                }

                var status = await CachedFileManager.CompleteUpdatesAsync(file);
                if (status is not (FileUpdateStatus.Complete or FileUpdateStatus.CompleteAndRenamed))
                {
                    throw new IOException($"导出场景文件未能完成保存（{status}）。");
                }
                if (_loaded)
                {
                    TrackToast(this.ShowSuccessToastRef($"已导出场景：{file.Name}"));
                }
            }
            finally
            {
                ViewModel.IsBusy = false;
            }
        }
        catch (Exception exception)
        {
            if (_loaded)
            {
                ShowError(exception);
            }
        }
    }

    private void OnCardDuplicateRequested(object? sender, SceneCardItem item) =>
        TryAction(() =>
        {
            var copy = ViewModel.DuplicateScene(item.Id);
            TrackToast(this.ShowSuccessToastRef($"已复制场景：{copy.Name}"));
        });

    private async void OnCardOpenFolderRequested(object? sender, SceneCardItem item)
    {
        try
        {
            var directory = _scenes.GetSceneDirectory(item.Id);
            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            if (!await Launcher.LaunchFolderAsync(folder))
            {
                ShowMessage("无法打开场景文件夹", folder.Path);
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void OnCardRenameRequested(object? sender, SceneCardItem item)
    {
        try
        {
            var name = await AskNameAsync("重命名场景", item.Name);
            if (name != null && _loaded)
            {
                ViewModel.RenameScene(item.Id, name);
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void OnCardDeleteRequested(object? sender, SceneCardItem item)
    {
        if (!item.CanDelete)
        {
            return;
        }

        try
        {
            ViewModel.DialogMessage = $"将删除“{item.Name}”及其专有资源。此操作无法撤销。";
            PrepareDialog(DeleteSceneDialog);
            if (await DeleteSceneDialog.ShowAsync() == ContentDialogResult.Primary && _loaded)
            {
                ViewModel.DeleteScene(item.Id);
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async Task<string?> AskNameAsync(string title, string initial)
    {
        ViewModel.DialogTitle = title;
        ViewModel.DialogName = initial;
        ViewModel.DialogNameError = "";
        PrepareDialog(NameDialog);
        return await NameDialog.ShowAsync() == ContentDialogResult.Primary ? ViewModel.DialogName.Trim() : null;
    }

    private void OnNameDialogPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.DialogName))
        {
            return;
        }

        args.Cancel = true;
        ViewModel.DialogNameError = "名称不能为空";
    }

    private void PrepareDialog(ContentDialog dialog)
    {
        dialog.DataContext = ViewModel;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            dialog.XamlRoot = PageRoot.XamlRoot;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "场景" : cleaned;
    }

    private void TryAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void ShowError(Exception exception) => TrackToast(this.ShowErrorToastRef("操作未完成", exception));

    private ToastMessage ShowMessage(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Error) =>
        TrackToast(this.ShowToastRef(new ToastMessage(title, message)
        {
            Severity = severity,
            AutoClose = false
        }));

    private ToastMessage TrackToast(ToastMessage message)
    {
        _pageToasts.RemoveWhere(toast => !toast.IsOpen);
        _pageToasts.Add(message);
        return message;
    }

    private void ClearPageToasts()
    {
        foreach (var message in _pageToasts)
        {
            message.Close();
        }

        _pageToasts.Clear();
        if (_window?.Content is WindowToastHost host)
        {
            host.Clear();
        }
    }
}
