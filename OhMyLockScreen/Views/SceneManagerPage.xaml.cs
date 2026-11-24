using Microsoft.UI.Xaml.Controls;
using OhMyLockScreen.Services.LockScreenProvider;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Models.UI;
using OhMyLockScreen.ViewModels;
using Windows.ApplicationModel.DataTransfer;
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
    private readonly ScenePackageImportQueue _importQueue;
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly HashSet<ToastMessage> _pageToasts = [];
    private Window? _window;
    private bool _loaded;
    private bool _processingImportQueue;
    private int _lockScreenStatusRequest;
    private bool? _lastLockScreenEnabled;

    public SceneManagerViewModel ViewModel { get; }

    public SceneManagerPage()
    {
        _scenes = IAppHost.GetRequiredService<ISceneService>();
        _lockScreenReplacement = IAppHost.GetRequiredService<LockScreenReplacementService>();
        _importQueue = IAppHost.GetRequiredService<ScenePackageImportQueue>();
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
        _importQueue.PendingChanged += OnPendingImportsChanged;

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

        _ = ProcessPendingImportsAsync();
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
        _importQueue.PendingChanged -= OnPendingImportsChanged;
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

            ToastMessage? progressToast = null;
            ViewModel.IsBusy = true;
            try
            {
                progressToast = ShowProgressToast("正在导入场景…");
                await using var stream = await file.OpenStreamForReadAsync();
                var imported = await ViewModel.ImportSceneAsync(stream);
                progressToast.Close();
                if (_loaded)
                {
                    TrackToast(this.ShowSuccessToastRef($"已导入场景：{imported.Name}"));
                }
            }
            finally
            {
                progressToast?.Close();
                ViewModel.IsBusy = false;
                if (_loaded)
                {
                    _ = ProcessPendingImportsAsync();
                }
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

    private void OnPageDragOver(object sender, DragEventArgs args)
    {
        if (!args.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var canImport = _loaded && !ViewModel.IsBusy;
        args.AcceptedOperation = canImport ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (canImport)
        {
            args.DragUIOverride.Caption = "导入场景";
            args.DragUIOverride.IsCaptionVisible = true;
            args.DragUIOverride.IsGlyphVisible = true;
        }

        args.Handled = true;
    }

    private async void OnPageDrop(object sender, DragEventArgs args)
    {
        if (!args.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        args.Handled = true;
        args.AcceptedOperation = DataPackageOperation.None;
        if (!_loaded || ViewModel.IsBusy || _window is not { } window)
        {
            return;
        }

        IReadOnlyList<IStorageItem>? droppedItems = null;
        var deferral = args.GetDeferral();
        try
        {
            args.AcceptedOperation = DataPackageOperation.Copy;
            droppedItems = await args.DataView.GetStorageItemsAsync();
        }
        catch (Exception exception)
        {
            if (_loaded)
            {
                ShowError(exception);
            }
        }
        finally
        {
            deferral.Complete();
        }

        if (droppedItems is { Count: > 0 } && _loaded && ReferenceEquals(_window, window))
        {
            _importQueue.Enqueue(window, droppedItems);
        }
    }

    private async void OnPendingImportsChanged(object? sender, Window window)
    {
        await OnViewAsync(() =>
        {
            if (ReferenceEquals(window, _window))
            {
                _ = ProcessPendingImportsAsync();
            }
        });
    }

    private async Task ProcessPendingImportsAsync()
    {
        if (!_loaded || _window == null || _processingImportQueue || ViewModel.IsBusy)
        {
            return;
        }

        _processingImportQueue = true;
        try
        {
            while (_loaded && _window is { } window && !ViewModel.IsBusy && _importQueue.HasPending(window))
            {
                IReadOnlyList<IStorageItem> supported;
                List<string> unsupportedNames;
                ContentDialogResult dialogResult;
                var candidates = new List<ScenePackageImportCandidate<StorageFile>>();

                await _dialogGate.WaitAsync();
                try
                {
                    if (!_loaded || !ReferenceEquals(_window, window) || ViewModel.IsBusy)
                    {
                        break;
                    }

                    if (!_importQueue.TryDequeue(window, out var batch))
                    {
                        continue;
                    }

                    (supported, unsupportedNames) = ScenePackageImport.PartitionItems(
                        batch,
                        static item => item is StorageFile,
                        static item => item.Name);

                    if (unsupportedNames.Count > 0)
                    {
                        var title = supported.Count == 0 ? "不支持的文件" : "已跳过不支持的文件";
                        ShowMessage(title, $"仅支持导入 .omls 场景包：{string.Join("、", unsupportedNames)}", InfoBarSeverity.Warning);
                    }

                    if (supported.Count == 0)
                    {
                        continue;
                    }

                    foreach (var file in supported.Cast<StorageFile>())
                    {
                        ScenePackageMetadata? metadata = null;
                        try
                        {
                            await using var stream = await file.OpenStreamForReadAsync();
                            metadata = await ScenePackageImport.TryReadMetadataAsync(stream);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            metadata = null;
                        }

                        candidates.Add(new ScenePackageImportCandidate<StorageFile>(file, file.Name, metadata));
                    }

                    if (!_loaded || !ReferenceEquals(_window, window))
                    {
                        break;
                    }

                    var (message, detail) = ScenePackageImport.FormatConfirmationPrompt(candidates);
                    ViewModel.DialogMessage = message;
                    ViewModel.DialogDetail = detail;
                    PrepareDialog(ImportSceneDialog);
                    dialogResult = await ImportSceneDialog.ShowAsync();
                }
                finally
                {
                    _dialogGate.Release();
                }

                if (dialogResult != ContentDialogResult.Primary || !_loaded || !ReferenceEquals(_window, window))
                {
                    continue;
                }

                await ImportPackageCandidatesAsync(candidates);
            }
        }
        catch (Exception exception)
        {
            if (_loaded)
            {
                ShowError(exception);
            }
        }
        finally
        {
            _processingImportQueue = false;
            if (_loaded && _window is { } pendingWindow && !ViewModel.IsBusy && _importQueue.HasPending(pendingWindow))
            {
                await OnViewAsync(() => _ = ProcessPendingImportsAsync());
            }
        }
    }

    private async Task ImportPackageCandidatesAsync(IReadOnlyList<ScenePackageImportCandidate<StorageFile>> candidates)
    {
        if (!_loaded || candidates.Count == 0)
        {
            return;
        }

        var imported = new List<SceneSettings>(candidates.Count);
        var failures = new List<(string FileName, Exception Error)>();

        ToastMessage? progressToast = null;
        ViewModel.IsBusy = true;
        try
        {
            progressToast = ShowProgressToast(candidates.Count > 1
                ? $"正在导入 {candidates.Count} 个场景…"
                : "正在导入场景…");
            foreach (var candidate in candidates)
            {
                if (!_loaded)
                {
                    return;
                }

                try
                {
                    await using var stream = await candidate.File.OpenStreamForReadAsync();
                    imported.Add(await ViewModel.ImportSceneAsync(stream));
                }
                catch (Exception exception)
                {
                    failures.Add((candidate.FileName, exception));
                }
            }
        }
        finally
        {
            progressToast?.Close();
            ViewModel.IsBusy = false;
        }

        if (!_loaded)
        {
            return;
        }

        if (imported.Count == 1)
        {
            TrackToast(this.ShowSuccessToastRef($"已导入场景：{imported[0].Name}"));
        }
        else if (imported.Count > 1)
        {
            TrackToast(this.ShowSuccessToastRef($"已导入 {imported.Count} 个场景"));
        }

        if (failures.Count == 1 && imported.Count == 0)
        {
            ShowError(failures[0].Error);
        }
        else if (failures.Count > 0)
        {
            var details = string.Join(Environment.NewLine, failures.Select(failure => $"{failure.FileName}：{failure.Error.Message}"));
            ShowMessage("部分场景未能导入", details, InfoBarSeverity.Error);
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

            ToastMessage? progressToast = null;
            ViewModel.IsBusy = true;
            try
            {
                progressToast = ShowProgressToast("正在导出场景…");
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

                progressToast.Close();
                if (_loaded)
                {
                    ShowExportSuccessToast(file);
                }
            }
            finally
            {
                progressToast?.Close();
                ViewModel.IsBusy = false;
                if (_loaded)
                {
                    _ = ProcessPendingImportsAsync();
                }
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

    private void ShowExportSuccessToast(StorageFile file)
    {
        var actionButton = (Button)((DataTemplate)Resources["RevealExportedFileButtonTemplate"]).LoadContent();
        var toast = new ToastMessage($"已导出场景：{file.Name}")
        {
            Severity = InfoBarSeverity.Success,
            ActionContent = actionButton
        };
        actionButton.Tag = new ExportedFileToastContext(file, toast);
        TrackToast(this.ShowToastRef(toast));
    }

    private async void OnRevealExportedFileClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: ExportedFileToastContext context } button || !button.IsEnabled)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            if (await RevealExportedFileAsync(context.File))
            {
                context.Toast.Close();
            }
            else if (_loaded)
            {
                ShowMessage(
                    "无法在文件资源管理器中显示文件",
                    string.IsNullOrWhiteSpace(context.File.Path) ? context.File.Name : context.File.Path);
            }
        }
        catch (Exception exception)
        {
            if (_loaded)
            {
                ShowError(exception);
            }
        }
        finally
        {
            if (context.Toast.IsOpen)
            {
                button.IsEnabled = true;
            }
        }
    }

    private static async Task<bool> RevealExportedFileAsync(StorageFile file)
    {
        var options = new FolderLauncherOptions();
        options.ItemsToSelect.Add(file);

        // LaunchFolderPathAsync 无需目标目录权限，可直接配合选择器返回的 StorageFile 高亮定位文件。
        if (!string.IsNullOrWhiteSpace(file.Path) && Path.GetDirectoryName(file.Path) is { Length: > 0 } folderPath)
        {
            return await Launcher.LaunchFolderPathAsync(folderPath, options);
        }

        if (await file.GetParentAsync() is { } folder)
        {
            return await Launcher.LaunchFolderAsync(folder, options);
        }

        return false;
    }

    private sealed record ExportedFileToastContext(StorageFile File, ToastMessage Toast);

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
            await _dialogGate.WaitAsync();
            try
            {
                if (!_loaded)
                {
                    return;
                }

                ViewModel.DialogMessage = $"将删除“{item.Name}”及其专有资源。此操作无法撤销。";
                PrepareDialog(DeleteSceneDialog);
                if (await DeleteSceneDialog.ShowAsync() == ContentDialogResult.Primary && _loaded)
                {
                    ViewModel.DeleteScene(item.Id);
                }
            }
            finally
            {
                _dialogGate.Release();
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async Task<string?> AskNameAsync(string title, string initial)
    {
        await _dialogGate.WaitAsync();
        try
        {
            if (!_loaded)
            {
                return null;
            }

            ViewModel.DialogTitle = title;
            ViewModel.DialogName = initial;
            ViewModel.DialogNameError = "";
            PrepareDialog(NameDialog);
            return await NameDialog.ShowAsync() == ContentDialogResult.Primary ? ViewModel.DialogName.Trim() : null;
        }
        finally
        {
            _dialogGate.Release();
        }
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

    private ToastMessage ShowProgressToast(string message) =>
        TrackToast(this.ShowToastRef(new ToastMessage(message)
        {
            AutoClose = false,
            CanUserClose = false
        }));

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
