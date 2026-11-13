using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml;

namespace OhMyLockScreen;

public sealed partial class SceneEditorPage
{
    private long _mediaImportRevision;

    private void OnMediaImportStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SceneEditorViewModel.Scene) or nameof(SceneEditorViewModel.IsPreview))
        {
            ClearTreePointer();
            _mediaImportRevision++;
            VideoPicker.InvalidateSelection();
            RefreshFeatureEditorTargets();
        }
    }

    private bool IsCurrentMediaImport(SceneSettings scene, long revision) =>
        _loaded && !ViewModel.IsPreview && ReferenceEquals(scene, ViewModel.Scene) && revision == _mediaImportRevision;

    private bool IsFileDrag(DragEventArgs args) =>
        _draggedLibraryItem == null && _draggedTreeItem == null
        && args.DataView.Contains(StandardDataFormats.StorageItems);

    private bool HandleFileDragOver(DragEventArgs args)
    {
        if (!IsFileDrag(args))
        {
            return false;
        }

        args.AcceptedOperation = _loaded && !ViewModel.IsPreview && ViewModel.Scene != null
            ? DataPackageOperation.Copy : DataPackageOperation.None;
        args.DragUIOverride.Caption = "导入图片或设置视频背景";
        args.Handled = true;
        return true;
    }

    private async void OnMediaFileDrop(object sender, DragEventArgs args)
    {
        if (!IsFileDrag(args))
        {
            return;
        }

        args.Handled = true;
        args.AcceptedOperation = DataPackageOperation.None;
        if (!_loaded || ViewModel.IsPreview || ViewModel.Scene is not { } scene)
        {
            return;
        }

        var deferral = args.GetDeferral();
        var revision = ++_mediaImportRevision;
        VideoPicker.InvalidateSelection();
        bool IsCurrent() => IsCurrentMediaImport(scene, revision);
        try
        {
            // Resolve once, before awaiting files or selecting newly inserted components.
            var tree = ReferenceEquals(sender, SceneTree);
            var target = ResolveMediaDropTarget(args, tree);
            var parentModel = target?.Parent.Model ?? ViewModel.GetCanvasLayer(null)?.Model ?? (object)scene;
            var index = target?.Index ?? SceneHierarchyEditing.GetChildren(parentModel)?.Count ?? 0;
            var dropParent = tree ? null : ViewModel.FindItem(parentModel);
            var dropPanel = dropParent == null ? null : FindLayoutPanel(dropParent);
            if (!tree && ReferenceEquals(parentModel, scene))
            {
                // A newly created first layer fills the viewport and has no realized panel yet.
                dropPanel = SceneViewport;
            }
            Point? dropPoint = dropPanel == null ? null
                : SceneViewport.TransformToVisual(dropPanel).TransformPoint(args.GetPosition(SceneViewport));
            args.AcceptedOperation = DataPackageOperation.Copy;
            var items = await args.DataView.GetStorageItemsAsync();
            if (!IsCurrent())
            {
                return;
            }

            async Task<bool> ImportImage(StorageFile file)
            {
                bool CanInsert() => IsCurrent() && ViewModel.FindItem(parentModel) != null;
                if (!CanInsert())
                {
                    return false;
                }

                var blobId = await ImportBlobAsync(file, scene.SceneId);
                var uri = _blobs.GetBlobUri(scene.SceneId, blobId)
                    ?? throw new FileNotFoundException("导入的场景图片不存在。");
                var (width, height) = await SceneImageDecoder.ReadSizeAsync(uri);
                if (!CanInsert())
                {
                    return false;
                }

                var component = ViewModel.AddImageComponent(file.DisplayName, blobId, width, height, parentModel, index)
                    ?? throw new InvalidOperationException("图片导入目标已失效。");
                var parent = ViewModel.FindItem(component)!.Parent!;
                parentModel = parent.Model;
                if (dropPoint is { } position)
                {
                    var panel = FindLayoutPanel(parent) ?? dropPanel;
                    if (panel != null)
                    {
                        ApplyCanvasDropPosition(component, panel, position);
                    }
                }
                index++;
                return true;
            }

            var result = await SceneMediaImport.RunAsync(
                items.OfType<StorageFile>(), file => file.Name, ImportImage,
                file => ImportVideoBackgroundAsync(file, scene, revision), IsCurrent);
            if (!IsCurrent())
            {
                return;
            }

            result.UnsupportedFiles.AddRange(items.Where(item => item is not StorageFile).Select(item => item.Name));
            ShowMediaImportResult(result);
        }
        catch (Exception exception)
        {
            if (IsCurrent())
            {
                ShowError(exception);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private (SceneTreeItem Parent, int Index)? ResolveMediaDropTarget(DragEventArgs args, bool tree)
    {
        if (tree)
        {
            return ResolveTreeDropTarget(args.GetPosition(SceneTree), LibraryDragPlaceholder);
        }

        var canvasPoint = args.GetPosition(SceneViewport);
        return ResolveCanvasDrop(canvasPoint, LibraryDragPlaceholder) is { } target
            ? (target.Parent, GetInsertionIndex(target.Panel, canvasPoint, null))
            : null;
    }

    private async Task<bool> ImportVideoBackgroundAsync(StorageFile file, SceneSettings scene, long revision)
    {
        var blobId = await ImportBlobAsync(file, scene.SceneId);
        if (!IsCurrentMediaImport(scene, revision))
        {
            return false;
        }

        scene.Background.BlobId = blobId;
        scene.Background.Mode = BackgroundMode.Video;
        return true;
    }

    private async Task<string> ImportBlobAsync(StorageFile file, Guid sceneId)
    {
        using var stream = await file.OpenStreamForReadAsync();
        return await _blobs.ImportAsync(sceneId, stream);
    }

    private void ShowMediaImportResult(SceneMediaImportResult result)
    {
        if (result.VideoName is { } video)
        {
            TrackToast(this.ShowSuccessToastRef($"已设置视频背景：{video}"));
        }

        var messages = new List<string>();
        if (result.SkippedVideos > 0)
        {
            messages.Add($"仅使用首个视频，已跳过其余 {result.SkippedVideos} 个视频。");
        }

        if (result.UnsupportedFiles.Count > 0)
        {
            messages.Add($"不支持的文件或文件夹：{string.Join("、", result.UnsupportedFiles)}");
        }

        messages.AddRange(result.FailedFiles);
        if (messages.Count > 0)
        {
            ShowMessage("部分文件未导入", string.Join(Environment.NewLine, messages), InfoBarSeverity.Warning);
        }
    }
}
