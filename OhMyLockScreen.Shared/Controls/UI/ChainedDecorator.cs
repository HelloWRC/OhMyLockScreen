using System.Runtime.CompilerServices;
using OhMyLockScreen.Shared.Abstractions.Controls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Markup;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>通用的单子节点 XAML 链式嵌套装饰器容器。</summary>
[ContentProperty(Name = nameof(Child))]
public sealed class ChainedDecorator : Control, IDecoratorHost
{
    public static readonly DependencyProperty ChildProperty = DependencyProperty.Register(
        nameof(Child),
        typeof(UIElement),
        typeof(ChainedDecorator),
        new PropertyMetadata(null, (sender, _) => ((ChainedDecorator)sender).Rebuild()));

    public static readonly DependencyProperty ChainProperty = DependencyProperty.Register(
        nameof(Chain),
        typeof(DecoratorChain),
        typeof(ChainedDecorator),
        new PropertyMetadata(null, (sender, _) => ((ChainedDecorator)sender).SyncChainAttachment()));

    private readonly List<DecoratorChain.DecoratorEntry> _mountedNodes = [];
    private DecoratorChain? _ownedChain;
    private DecoratorChain? _attachedChain;
    private Border? _hostSlot;
    private Border? _mountedSlot;
    private UIElement? _mountedCoreChild;
    private bool _isUnloaded;

    public UIElement? Child
    {
        get => (UIElement?)GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    public DecoratorChain? Chain
    {
        get => (DecoratorChain?)GetValue(ChainProperty);
        set => SetValue(ChainProperty, value);
    }

    public DecoratorChain Decorators => Chain ?? GetOrCreateOwnedChain();

    public ChainedDecorator()
    {
        DefaultStyleKey = typeof(ChainedDecorator);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public IDecoratorRegistration Register(UIElement element, int priority = DecoratorPriorities.Default)
        => Decorators.Register(element, priority);

    [OverloadResolutionPriority(1)]
    public IDecoratorRegistration Register(ISingleChildDecorator decorator, int priority = DecoratorPriorities.Default)
        => Decorators.Register(decorator, priority);

    public IDecoratorRegistration Register(
        UIElement element,
        Action<UIElement?> setChild,
        int priority = DecoratorPriorities.Default)
        => Decorators.Register(element, setChild, priority);

    public ISharedDecoratorRegistration<TElement> AcquireShared<TElement>(
        object key,
        Func<TElement> factory,
        int priority = DecoratorPriorities.Default)
        where TElement : UIElement
        => Decorators.AcquireShared(key, factory, priority);

    protected override void OnApplyTemplate()
    {
        TeardownMountedTree();
        DetachFromAttachedChain();
        base.OnApplyTemplate();
        _hostSlot = GetTemplateChild("PART_Host") as Border;
        SyncChainAttachment();
    }

    internal void OnDetachedFromChain(DecoratorChain chain)
    {
        if (ReferenceEquals(_attachedChain, chain))
        {
            _attachedChain = null;
        }

        TeardownMountedTree();
    }

    internal void Rebuild()
    {
        if (_isUnloaded || _hostSlot is not { } hostSlot)
        {
            TeardownMountedTree();
            return;
        }

        var desired = _attachedChain?.GetOrderedEntriesSnapshot()
            ?? Array.Empty<DecoratorChain.DecoratorEntry>();
        var coreChild = Child;

        if (IsMountedStateUnchanged(hostSlot, coreChild, desired))
        {
            return;
        }

        var prefixLen = ComputePreservedPrefixLength(hostSlot, desired);
        if (prefixLen > 0)
        {
            RebuildPreservingPrefix(hostSlot, coreChild, desired, prefixLen);
            return;
        }

        TeardownMountedTree();

        var linkedCount = 0;
        try
        {
            var current = coreChild;
            for (var index = desired.Length - 1; index >= 0; index--)
            {
                desired[index].SetChild(current);
                linkedCount++;
                current = desired[index].Element;
            }

            hostSlot.Child = current;
            _mountedSlot = hostSlot;
            _mountedCoreChild = coreChild;
            _mountedNodes.AddRange(desired);
        }
        catch
        {
            // 按由内向外的顺序清理已连接的节点，避免子元素残留旧父级引用后恢复直挂。
            var firstLinked = desired.Length - linkedCount;
            for (var index = desired.Length - 1; index >= firstLinked; index--)
            {
                try
                {
                    desired[index].SetChild(null);
                }
                catch
                {
                }
            }

            try
            {
                hostSlot.Child = coreChild;
                _mountedSlot = hostSlot;
                _mountedCoreChild = coreChild;
            }
            catch
            {
                _mountedSlot = null;
                _mountedCoreChild = null;
            }

            throw;
        }
    }

    private int ComputePreservedPrefixLength(Border hostSlot, DecoratorChain.DecoratorEntry[] desired)
    {
        if (!ReferenceEquals(_mountedSlot, hostSlot) || _mountedNodes.Count == 0 || desired.Length == 0)
        {
            return 0;
        }

        var maxPrefix = Math.Min(_mountedNodes.Count, desired.Length);
        var prefixLen = 0;
        while (prefixLen < maxPrefix
            && ReferenceEquals(_mountedNodes[prefixLen].Element, desired[prefixLen].Element))
        {
            prefixLen++;
        }

        return prefixLen;
    }

    private void RebuildPreservingPrefix(
        Border hostSlot,
        UIElement? coreChild,
        DecoratorChain.DecoratorEntry[] desired,
        int prefixLen)
    {
        // 保留未变外层节点在视觉树中的挂载状态，仅自内向外拆换发生变动的内层后缀。
        for (var index = _mountedNodes.Count - 1; index >= prefixLen - 1; index--)
        {
            try
            {
                _mountedNodes[index].SetChild(null);
            }
            catch
            {
            }
        }

        for (var index = 0; index < prefixLen; index++)
        {
            _mountedNodes[index] = desired[index];
        }

        if (_mountedNodes.Count > prefixLen)
        {
            _mountedNodes.RemoveRange(prefixLen, _mountedNodes.Count - prefixLen);
        }

        _mountedCoreChild = null;

        var linkedCount = 0;
        try
        {
            var current = coreChild;
            for (var index = desired.Length - 1; index >= prefixLen; index--)
            {
                desired[index].SetChild(current);
                linkedCount++;
                current = desired[index].Element;
            }

            desired[prefixLen - 1].SetChild(current);
            _mountedCoreChild = coreChild;
            for (var index = prefixLen; index < desired.Length; index++)
            {
                _mountedNodes.Add(desired[index]);
            }
        }
        catch
        {
            var firstLinked = desired.Length - linkedCount;
            for (var index = desired.Length - 1; index >= firstLinked; index--)
            {
                try
                {
                    desired[index].SetChild(null);
                }
                catch
                {
                }
            }

            try
            {
                desired[prefixLen - 1].SetChild(coreChild);
                _mountedCoreChild = coreChild;
            }
            catch
            {
                TeardownMountedTree();
                try
                {
                    hostSlot.Child = coreChild;
                    _mountedSlot = hostSlot;
                    _mountedCoreChild = coreChild;
                }
                catch
                {
                    _mountedSlot = null;
                    _mountedCoreChild = null;
                }
            }

            throw;
        }
    }

    private DecoratorChain GetOrCreateOwnedChain()
    {
        if (_ownedChain == null)
        {
            _ownedChain = new DecoratorChain();
            SyncChainAttachment();
        }

        return _ownedChain;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _isUnloaded = false;
        SyncChainAttachment();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded)
        {
            return;
        }

        _isUnloaded = true;
        DetachFromAttachedChain();
        TeardownMountedTree();
    }

    private void SyncChainAttachment()
    {
        if (_isUnloaded || _hostSlot == null)
        {
            DetachFromAttachedChain();
            TeardownMountedTree();
            return;
        }

        var targetChain = Chain ?? _ownedChain;
        if (!ReferenceEquals(_attachedChain, targetChain))
        {
            DetachFromAttachedChain();
            _attachedChain = targetChain;
            if (targetChain != null)
            {
                targetChain.AttachHost(this);
                return;
            }
        }

        Rebuild();
    }

    private void DetachFromAttachedChain()
    {
        var previous = _attachedChain;
        _attachedChain = null;
        previous?.DetachHost(this);
    }

    private bool IsMountedStateUnchanged(
        Border hostSlot,
        UIElement? coreChild,
        DecoratorChain.DecoratorEntry[] desired)
    {
        if (!ReferenceEquals(_mountedSlot, hostSlot)
            || !ReferenceEquals(_mountedCoreChild, coreChild)
            || _mountedNodes.Count != desired.Length)
        {
            return false;
        }

        for (var index = 0; index < desired.Length; index++)
        {
            if (!ReferenceEquals(_mountedNodes[index].Element, desired[index].Element))
            {
                return false;
            }
        }

        return true;
    }

    private void TeardownMountedTree()
    {
        // 先由内向外断开各包装层，再清空宿主槽位，保证 ContentControl 在视觉树活跃时同步释放子元素。
        for (var index = _mountedNodes.Count - 1; index >= 0; index--)
        {
            try
            {
                _mountedNodes[index].SetChild(null);
            }
            catch
            {
            }
        }

        _mountedNodes.Clear();
        if (_mountedSlot != null)
        {
            try
            {
                _mountedSlot.Child = null;
            }
            catch
            {
            }

            _mountedSlot = null;
        }

        _mountedCoreChild = null;
    }
}
