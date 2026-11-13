using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.ViewModels;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using TreeViewItem = Microsoft.UI.Xaml.Controls.TreeViewItem;

namespace OhMyLockScreen;

public sealed partial class SceneEditorPage
{
    private void OnTreePreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_window?.CoreWindow is not { } coreWindow
            || args.Key is not (VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right))
        {
            return;
        }

        bool IsDown(VirtualKey key) => (coreWindow.GetKeyState(key) & CoreVirtualKeyStates.Down) != 0;
        if (!IsDown(VirtualKey.Menu) || !IsDown(VirtualKey.Shift) || IsDown(VirtualKey.Control))
        {
            return;
        }

        // Handle before TreeView performs its own node-only keyboard reorder.
        args.Handled = true;
        if (!_loaded || ViewModel.IsPreview
            || FindParent<TreeViewItem>(args.OriginalSource as DependencyObject) is not { } container
            || SceneTree.ItemFromContainer(container) is not SceneTreeItem item
            || ViewModel.FindItem(item.Model) == null)
        {
            return;
        }

        TryAction(() =>
        {
            ViewModel.Select(item.Model);
            ViewModel.MoveSelection(args.Key is VirtualKey.Up or VirtualKey.Left ? -1 : 1);
            SceneTree.UpdateLayout();
            if (ViewModel.SelectedItem is { } selected)
            {
                (SceneTree.ContainerFromItem(selected) as Control)?.Focus(FocusState.Keyboard);
            }
        });
    }

    private void QueueEditorAction(Action action)
    {
        var window = _window;
        var scene = ViewModel.Scene;
        if (window == null || scene == null || ViewModel.IsPreview)
        {
            return;
        }

        _ = window.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            if (_loaded && ReferenceEquals(_window, window)
                && ReferenceEquals(ViewModel.Scene, scene) && !ViewModel.IsPreview)
            {
                TryAction(action);
            }
        });
    }

    private (SceneTreeItem Parent, int Index)? ResolveTreeDropTarget(
        SceneTreeItem target,
        object model,
        TreeDropRegion region)
    {
        SceneTreeItem? parent = null;
        var index = 0;
        if (target.Model is SceneSettings || region == TreeDropRegion.Inside)
        {
            parent = target;
            index = SceneHierarchyEditing.GetChildren(parent.Model)?.Count ?? 0;
        }
        else if (target.Parent != null)
        {
            parent = target.Parent;
            var targetIndex = SceneHierarchyEditing.GetChildren(parent.Model)?.IndexOf(target.Model) ?? -1;
            if (targetIndex < 0)
            {
                return null;
            }

            index = targetIndex + (region == TreeDropRegion.Before ? 0 : 1);
        }

        if (parent == null
            || !SceneHierarchyEditing.CanAccept(parent.Model, model)
            || SceneHierarchyEditing.GetChildren(parent.Model) is not { } children)
        {
            return null;
        }

        if (_draggedTreeItem is { } source && ReferenceEquals(source.Model, model)
            && ReferenceEquals(source.Parent, parent))
        {
            var sourceIndex = children.IndexOf(source.Model);
            if (sourceIndex < 0)
            {
                return null;
            }

            if (sourceIndex < index)
            {
                index--;
            }

            index = Math.Clamp(index, 0, children.Count - 1);
            return sourceIndex == index ? null : (parent, index);
        }

        return (parent, Math.Clamp(index, 0, children.Count));
    }

    private static TreeDropRegion GetTreeDropRegion(SceneTreeItem target, double ratio)
    {
        if (SceneHierarchyEditing.GetChildren(target.Model) == null)
        {
            return ratio < 0.5 ? TreeDropRegion.Before : TreeDropRegion.After;
        }

        return ratio < 0.2 ? TreeDropRegion.Before : ratio > 0.8 ? TreeDropRegion.After : TreeDropRegion.Inside;
    }

    private enum TreeDropRegion
    {
        Before,
        Inside,
        After
    }
}
