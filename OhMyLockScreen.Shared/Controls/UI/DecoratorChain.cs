using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>表示可被 <see cref="DecoratorChain"/> 编排的单子节点视觉装饰器。</summary>
public interface ISingleChildDecorator
{
    UIElement Element { get; }

    void SetChild(UIElement? child);
}

/// <summary>表示向 <see cref="DecoratorChain"/> 注册的独立装饰节点租约。</summary>
public interface IDecoratorRegistration : IDisposable
{
    UIElement Element { get; }

    /// <summary>节点优先级，数值越小越靠近外层，数值越大越靠近核心子节点。</summary>
    int Priority { get; set; }
}

/// <summary>表示按键共享的装饰节点引用计数租约。</summary>
public interface ISharedDecoratorRegistration<out TElement> : IDisposable where TElement : UIElement
{
    TElement Element { get; }
}

/// <summary>内置装饰器的标准层级优先级常量（升序由外到内）。</summary>
public static class DecoratorPriorities
{
    public const int Parallax = -200;
    public const int Shadow = -100;
    public const int EditorOverlay = -50;
    public const int Default = 0;
    public const int Border = 200;
}

/// <summary>内置共享装饰节点使用的标准键值。</summary>
public static class DecoratorKeys
{
    public static object Border { get; } = new();
}

/// <summary>管理单子节点装饰器链的注册、共享、优先级排序与宿主同步。</summary>
[Bindable]
public sealed class DecoratorChain
{
    internal sealed class DecoratorEntry(
        UIElement element,
        Action<UIElement?> setChild,
        int priority,
        long sequence,
        object? sharedKey = null,
        int refCount = 1)
    {
        public UIElement Element { get; } = element;

        public Action<UIElement?> SetChild { get; } = setChild;

        public int Priority { get; set; } = priority;

        public long Sequence { get; } = sequence;

        public object? SharedKey { get; } = sharedKey;

        public int RefCount { get; set; } = refCount;

        public bool IsRemoved { get; set; }
    }

    private readonly List<DecoratorEntry> _entries = [];
    private readonly Dictionary<object, DecoratorEntry> _sharedEntries = [];
    private DecoratorEntry[]? _orderedCache;
    private ChainedDecorator? _host;
    private long _nextSequence;
    private int _deferDepth;
    private bool _dirty;

    /// <summary>当前链中按外到内顺序排列的装饰节点快照。</summary>
    public IReadOnlyList<UIElement> Elements
    {
        get
        {
            var ordered = GetOrderedEntriesSnapshot();
            if (ordered.Length == 0)
            {
                return Array.Empty<UIElement>();
            }

            var result = new UIElement[ordered.Length];
            for (var index = 0; index < ordered.Length; index++)
            {
                result[index] = ordered[index].Element;
            }

            return result;
        }
    }

    /// <summary>当链中的装饰节点集合或排序发生实际变化时触发。</summary>
    public event EventHandler? Changed;

    public IDecoratorRegistration Register(UIElement element, int priority = DecoratorPriorities.Default)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is ISingleChildDecorator decorator)
        {
            return Register(decorator, priority);
        }

        return Register(element, ResolveSetChild(element), priority);
    }

    [OverloadResolutionPriority(1)]
    public IDecoratorRegistration Register(ISingleChildDecorator decorator, int priority = DecoratorPriorities.Default)
    {
        ArgumentNullException.ThrowIfNull(decorator);
        ArgumentNullException.ThrowIfNull(decorator.Element);
        return Register(decorator.Element, decorator.SetChild, priority);
    }

    public IDecoratorRegistration Register(
        UIElement element,
        Action<UIElement?> setChild,
        int priority = DecoratorPriorities.Default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(setChild);
        EnsureElementNotRegistered(element);

        var entry = new DecoratorEntry(element, setChild, priority, ++_nextSequence);
        _entries.Add(entry);
        _orderedCache = null;
        try
        {
            NotifyChanged();
        }
        catch
        {
            entry.IsRemoved = true;
            _entries.Remove(entry);
            _orderedCache = null;
            throw;
        }

        return new DecoratorRegistration(this, entry);
    }

    /// <summary>按指定键获取或创建共享装饰节点；多处同时租用时复用同一节点，全部释放后移除。</summary>
    public ISharedDecoratorRegistration<TElement> AcquireShared<TElement>(
        object key,
        Func<TElement> factory,
        int priority = DecoratorPriorities.Default)
        where TElement : UIElement
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (_sharedEntries.TryGetValue(key, out var existing))
        {
            if (existing.Element is not TElement typedElement)
            {
                throw new InvalidOperationException(
                    $"共享键已绑定到 {existing.Element.GetType().FullName}，无法按 {typeof(TElement).FullName} 获取。");
            }

            existing.RefCount++;
            return new SharedDecoratorRegistration<TElement>(this, existing, typedElement);
        }

        var element = factory()
            ?? throw new InvalidOperationException("共享装饰器工厂不能返回 null。");
        EnsureElementNotRegistered(element);
        var setChild = ResolveSetChild(element);

        var entry = new DecoratorEntry(element, setChild, priority, ++_nextSequence, key, refCount: 1);
        _entries.Add(entry);
        _sharedEntries.Add(key, entry);
        _orderedCache = null;
        try
        {
            NotifyChanged();
        }
        catch
        {
            entry.IsRemoved = true;
            _entries.Remove(entry);
            _sharedEntries.Remove(key);
            _orderedCache = null;
            throw;
        }

        return new SharedDecoratorRegistration<TElement>(this, entry, element);
    }

    public bool TryGetShared<TElement>(object key, [NotNullWhen(true)] out TElement? element)
        where TElement : UIElement
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_sharedEntries.TryGetValue(key, out var entry) && !entry.IsRemoved && entry.Element is TElement typed)
        {
            element = typed;
            return true;
        }

        element = null;
        return false;
    }

    /// <summary>在作用域结束前合并多次节点变更，仅在最外层退出时触发一次重建与通知。</summary>
    public IDisposable DeferUpdates()
    {
        _deferDepth++;
        return new DeferScope(this);
    }

    internal void AttachHost(ChainedDecorator host)
    {
        if (ReferenceEquals(_host, host))
        {
            host.Rebuild();
            return;
        }

        if (_host is { } previous)
        {
            _host = null;
            previous.OnDetachedFromChain(this);
        }

        _host = host;
        host.Rebuild();
    }

    internal void DetachHost(ChainedDecorator host)
    {
        if (ReferenceEquals(_host, host))
        {
            _host = null;
        }
    }

    internal DecoratorEntry[] GetOrderedEntriesSnapshot()
    {
        if (_orderedCache != null)
        {
            return _orderedCache;
        }

        if (_entries.Count == 0)
        {
            return _orderedCache = Array.Empty<DecoratorEntry>();
        }

        var ordered = _entries.ToArray();
        Array.Sort(ordered, static (left, right) =>
        {
            var priorityCompare = left.Priority.CompareTo(right.Priority);
            return priorityCompare != 0 ? priorityCompare : left.Sequence.CompareTo(right.Sequence);
        });
        return _orderedCache = ordered;
    }

    private void EnsureElementNotRegistered(UIElement element)
    {
        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.Element, element))
            {
                throw new InvalidOperationException("该元素已注册到当前装饰器链，禁止重复注册。");
            }
        }
    }

    private static Action<UIElement?> ResolveSetChild(UIElement element) => element switch
    {
        ISingleChildDecorator decorator => decorator.SetChild,
        Border border => child => border.Child = child,
        ContentControl contentControl => child => contentControl.Content = child,
        ContentPresenter contentPresenter => child => contentPresenter.Content = child,
        Viewbox viewbox => child => viewbox.Child = child,
        Panel panel => child =>
        {
            panel.Children.Clear();
            if (child != null)
            {
                panel.Children.Add(child);
            }
        },
        _ => throw new NotSupportedException(
            $"类型 {element.GetType().FullName} 不支持自动子节点适配，请改用显式 setChild 委托或实现 {nameof(ISingleChildDecorator)}。")
    };

    private void UpdatePriority(DecoratorEntry entry, int priority)
    {
        if (entry.IsRemoved || entry.Priority == priority)
        {
            return;
        }

        entry.Priority = priority;
        _orderedCache = null;
        NotifyChanged();
    }

    private void RemoveEntry(DecoratorEntry entry)
    {
        if (entry.IsRemoved)
        {
            return;
        }

        entry.IsRemoved = true;
        _entries.Remove(entry);
        if (entry.SharedKey != null)
        {
            _sharedEntries.Remove(entry.SharedKey);
        }

        _orderedCache = null;
        NotifyChanged();
    }

    private void ReleaseShared(DecoratorEntry entry)
    {
        if (entry.IsRemoved)
        {
            return;
        }

        entry.RefCount--;
        if (entry.RefCount <= 0)
        {
            RemoveEntry(entry);
        }
    }

    private void EndDefer()
    {
        if (_deferDepth == 0)
        {
            return;
        }

        _deferDepth--;
        if (_deferDepth == 0 && _dirty)
        {
            _dirty = false;
            NotifyChangedCore();
        }
    }

    private void NotifyChanged()
    {
        if (_deferDepth > 0)
        {
            _dirty = true;
            return;
        }

        NotifyChangedCore();
    }

    private void NotifyChangedCore()
    {
        _host?.Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class DecoratorRegistration(DecoratorChain owner, DecoratorEntry entry) : IDecoratorRegistration
    {
        private bool _disposed;

        public UIElement Element => entry.Element;

        public int Priority
        {
            get => entry.Priority;
            set
            {
                if (!_disposed)
                {
                    owner.UpdatePriority(entry, value);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.RemoveEntry(entry);
        }
    }

    private sealed class SharedDecoratorRegistration<TElement>(
        DecoratorChain owner,
        DecoratorEntry entry,
        TElement element) : ISharedDecoratorRegistration<TElement>
        where TElement : UIElement
    {
        private bool _disposed;

        public TElement Element { get; } = element;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.ReleaseShared(entry);
        }
    }

    private sealed class DeferScope(DecoratorChain owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.EndDefer();
        }
    }
}
