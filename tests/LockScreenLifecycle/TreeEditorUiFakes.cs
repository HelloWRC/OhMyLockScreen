using OhMyLockScreen.ViewModels;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;

namespace Windows.UI.Core
{
    [Flags]
    public enum CoreVirtualKeyStates
    {
        None = 0,
        Down = 1
    }

    public sealed partial class CoreWindow
    {
        internal HashSet<VirtualKey> KeysDown { get; } = [];
        public CoreVirtualKeyStates GetKeyState(VirtualKey key) =>
            KeysDown.Contains(key) ? CoreVirtualKeyStates.Down : CoreVirtualKeyStates.None;
    }
}

namespace Windows.UI.Xaml
{
    public enum FocusState
    {
        Keyboard
    }
}

namespace Windows.UI.Xaml.Input
{
    public sealed class KeyRoutedEventArgs(VirtualKey key, DependencyObject source) : EventArgs
    {
        public VirtualKey Key { get; } = key;
        public DependencyObject OriginalSource { get; } = source;
        public bool Handled { get; set; }
    }
}

namespace Windows.UI.Xaml.Controls
{
    public partial class Control
    {
        public bool HasKeyboardFocus { get; private set; }
        public bool Focus(FocusState state) => HasKeyboardFocus = true;
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class TreeViewItem : Windows.UI.Xaml.Controls.Control;

    public sealed class TreeView
    {
        private readonly Dictionary<object, TreeViewItem> _containers = [];
        public void UpdateLayout()
        {
        }
        public object? ItemFromContainer(DependencyObject container) => ((TreeViewItem)container).DataContext;
        public DependencyObject ContainerFromItem(object item)
        {
            if (!_containers.TryGetValue(item, out var container))
            {
                _containers[item] = container = new TreeViewItem { DataContext = item };
            }

            return container;
        }
    }
}

namespace OhMyLockScreen
{
    public sealed partial class SceneEditorPage
    {
        public SceneEditorViewModel ViewModel { get; private set; } = null!;
        internal Microsoft.UI.Xaml.Controls.TreeView SceneTree { get; } = new();
        private SceneTreeItem? _draggedTreeItem;
        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject => child as T;
        private static void TryAction(Action action) => action();

        internal void LoadTree(SceneEditorViewModel editor)
        {
            ViewModel = editor;
            _window = Window.Current;
            _loaded = true;
        }

        internal bool PressTreeKey(SceneTreeItem item, VirtualKey key, bool alt = true, bool shift = true, bool control = false)
        {
            var keys = _window!.CoreWindow.KeysDown;
            keys.Clear();
            if (alt)
            {
                keys.Add(VirtualKey.Menu);
            }

            if (shift)
            {
                keys.Add(VirtualKey.Shift);
            }

            if (control)
            {
                keys.Add(VirtualKey.Control);
            }

            var args = new KeyRoutedEventArgs(key, SceneTree.ContainerFromItem(item));
            OnTreePreviewKeyDown(SceneTree, args);
            return args.Handled;
        }

        internal void QueueTreeAction(Action action) => QueueEditorAction(action);

        internal (SceneTreeItem Parent, int Index)? ResolveTreeDrop(SceneTreeItem source, SceneTreeItem target, double rowPosition)
        {
            _draggedTreeItem = source;
            return ResolveTreeDropTarget(target, source.Model, GetTreeDropRegion(target, rowPosition));
        }
    }
}
