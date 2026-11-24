using System.Runtime.CompilerServices;
using Windows.Storage;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Services.Scene;

/// <summary>按视图窗口缓存待处理的 .omls 场景包导入队列。</summary>
internal sealed class ScenePackageImportQueue
{
    private readonly object _sync = new();
    private readonly ConditionalWeakTable<Window, Queue<IReadOnlyList<IStorageItem>>> _pending = new();

    internal event EventHandler<Window>? PendingChanged;

    internal void Enqueue(Window window, IReadOnlyList<IStorageItem> items)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        var snapshot = items.ToArray();
        lock (_sync)
        {
            var queue = _pending.GetOrCreateValue(window);
            queue.Enqueue(snapshot);
        }

        PendingChanged?.Invoke(this, window);
    }

    internal bool TryDequeue(Window window, out IReadOnlyList<IStorageItem> items)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_sync)
        {
            if (_pending.TryGetValue(window, out var queue) && queue.TryDequeue(out var next))
            {
                items = next;
                return true;
            }
        }

        items = [];
        return false;
    }

    internal bool HasPending(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_sync)
        {
            return _pending.TryGetValue(window, out var queue) && queue.Count > 0;
        }
    }

    internal void Clear(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_sync)
        {
            if (_pending.TryGetValue(window, out var queue))
            {
                queue.Clear();
            }
        }
    }
}
