using System.Collections;
using System.Collections.ObjectModel;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Models.Scene;

namespace OhMyLockScreen.Services.Scene;

/// <summary>集中约束场景、图层与容器组件之间的层级变更。</summary>
internal static class SceneHierarchyEditing
{
    internal static IList? GetChildren(object? model) => model switch
    {
        SceneSettings scene => scene.Layers,
        LayerSettings layer => layer.Children,
        ComponentSettings { Settings: IComponentContainerSettings container } => container.Children,
        _ => null
    };

    internal static bool CanAccept(object? parent, object item) =>
        !ReferenceEquals(parent, item)
        && parent switch
        {
            SceneSettings => item is LayerSettings,
            LayerSettings => item is ComponentSettings,
            ComponentSettings { Settings: IComponentContainerSettings } => item is ComponentSettings,
            _ => false
        }
        && !Contains(item, parent);

    internal static bool TryMove(object item, object? sourceParent, object targetParent, int index)
    {
        if (!CanAccept(targetParent, item)
            || GetChildren(sourceParent) is not { } source
            || GetChildren(targetParent) is not { } target)
        {
            return false;
        }

        var sourceIndex = source.IndexOf(item);
        if (sourceIndex < 0)
        {
            return false;
        }

        index = Math.Clamp(index, 0, target.Count);
        if (ReferenceEquals(source, target))
        {
            if (sourceIndex < index)
            {
                index--;
            }

            if (sourceIndex == index)
            {
                return false;
            }

            source.RemoveAt(sourceIndex);
            target.Insert(index, item);
            return true;
        }

        source.RemoveAt(sourceIndex);
        target.Insert(index, item);
        return true;
    }

    internal static bool TryMove<T>(ObservableCollection<T> items, T item, int index)
        where T : class
    {
        var source = items.IndexOf(item);
        index = Math.Clamp(index, 0, items.Count - 1);
        if (source < 0 || source == index)
        {
            return false;
        }

        items.Move(source, index);
        return true;
    }

    private static bool Contains(object? parent, object? item)
    {
        if (parent == null)
        {
            return false;
        }

        if (ReferenceEquals(parent, item))
        {
            return true;
        }

        return GetChildren(parent) is { } children
            && children.Cast<object>().Any(child => Contains(child, item));
    }
}
