using System.Numerics;
using System.Runtime.Versioning;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.Composition;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Helpers.UI;

/// <summary>协调 ItemsRepeater 卡片的进入动画与 Fluent 位移 Composition 隐式过渡。</summary>
public sealed class SceneListMotionController(ItemsRepeater repeater)
{
    private const string OffsetPropertyName = "Offset";
    private const string TranslationPropertyName = "Translation";
    private const string OpacityPropertyName = "Opacity";

    private readonly ItemsRepeater _repeater = repeater ?? throw new ArgumentNullException(nameof(repeater));
    private readonly UISettings _uiSettings = new();
    private readonly HashSet<UIElement> _pendingEntrance = [];
    private readonly Dictionary<UIElement, int> _pendingOffsetArming = [];
    private readonly HashSet<UIElement> _armedCards = [];
    private readonly Dictionary<UIElement, int> _activeEntranceTokens = [];

    private CoreDispatcher? _dispatcher;
    private Compositor? _compositor;
    private CubicBezierEasingFunction? _entranceEasing;
    private LinearEasingFunction? _opacityEasing;
    private CubicBezierEasingFunction? _offsetEasing;
    private ImplicitAnimationCollection? _offsetImplicitAnimations;
    private int _entranceBatchToken;
    private bool _attached;
    private bool _layoutHooked;
    private bool _renderingHooked;
    private bool _uiSettingsHooked;
    private bool _entranceWaitingForLayout;

    public void Attach(CoreDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (_attached)
        {
            return;
        }

        _attached = true;
        _dispatcher = dispatcher;
        _repeater.ElementPrepared += OnElementPrepared;
        _repeater.ElementClearing += OnElementClearing;

        if (!_uiSettingsHooked && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
            _uiSettingsHooked = true;
        }
    }

    public void QueueRealizedCardsEntrance()
    {
        if (!_attached)
        {
            return;
        }

        var count = _repeater.ItemsSourceView?.Count ?? 0;
        var queuedAny = false;
        for (var index = 0; index < count; index++)
        {
            if (_repeater.TryGetElement(index) is { } element)
            {
                PrepareElementForEntrance(element);
                queuedAny |= _pendingEntrance.Contains(element);
            }
        }

        if (queuedAny)
        {
            // 若页面从 NavigationCache 恢复且数据未变，不会触发布局更新，留出一帧后由 OnRendering 兜底触发。
            _entranceWaitingForLayout = true;
        }
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _dispatcher = null;
        _entranceWaitingForLayout = false;

        if (_uiSettingsHooked && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
            _uiSettingsHooked = false;
        }

        _repeater.ElementPrepared -= OnElementPrepared;
        _repeater.ElementClearing -= OnElementClearing;
        ResetAllTrackedAndRealizedElements();
        UpdateHooks();
    }

    private void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (_attached)
        {
            PrepareElementForEntrance(args.Element);
        }
    }

    private void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        _pendingEntrance.Remove(args.Element);
        _pendingOffsetArming.Remove(args.Element);
        _armedCards.Remove(args.Element);
        ResetElementVisual(args.Element);
        UpdateHooks();
    }

    private void PrepareElementForEntrance(UIElement element)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);

        // 先解除 Offset 隐式动画并停止旧过渡，防止首次布局或复用元素从 (0, 0) / 旧槽位飞入。
        visual.ImplicitAnimations = null;
        visual.StopAnimation(OffsetPropertyName);
        visual.StopAnimation(TranslationPropertyName);
        visual.StopAnimation(OpacityPropertyName);
        _armedCards.Remove(element);
        _activeEntranceTokens.Remove(element);

        if (!_uiSettings.AnimationsEnabled)
        {
            visual.Opacity = 1f;
            visual.Properties.InsertVector3(TranslationPropertyName, Vector3.Zero);
            _pendingEntrance.Remove(element);
            _pendingOffsetArming.Remove(element);
            UpdateHooks();
            return;
        }

        visual.Opacity = 0f;
        visual.Properties.InsertVector3(TranslationPropertyName, new Vector3(0f, SceneListMotion.EntranceOffsetY, 0f));
        _pendingEntrance.Add(element);
        _pendingOffsetArming[element] = SceneListMotion.OffsetArmRenderTicks;
        UpdateHooks();
    }

    private void OnRepeaterLayoutUpdated(object? sender, object args)
    {
        if (!_attached || _pendingEntrance.Count == 0)
        {
            return;
        }

        _entranceWaitingForLayout = false;
        FlushPendingEntranceAnimations();
        UpdateHooks();
    }

    private void OnRendering(object? sender, object args)
    {
        if (!_attached)
        {
            UpdateHooks();
            return;
        }

        if (_pendingEntrance.Count > 0)
        {
            if (_entranceWaitingForLayout)
            {
                _entranceWaitingForLayout = false;
            }
            else
            {
                FlushPendingEntranceAnimations();
            }
        }

        if (_pendingOffsetArming.Count > 0)
        {
            AdvanceOffsetArming();
        }

        UpdateHooks();
    }

    private void FlushPendingEntranceAnimations()
    {
        if (_pendingEntrance.Count == 0)
        {
            return;
        }

        var snapshot = _pendingEntrance.ToArray();
        _pendingEntrance.Clear();

        var batch = SceneListMotion.OrderEntranceBatch(snapshot, _repeater.GetElementIndex);
        foreach (var candidate in snapshot)
        {
            if (_repeater.GetElementIndex(candidate) < 0)
            {
                ResetElementVisual(candidate);
            }
        }

        if (batch.Count == 0)
        {
            return;
        }

        if (!_uiSettings.AnimationsEnabled)
        {
            foreach (var element in batch)
            {
                ResetElementVisual(element);
            }
            return;
        }

        var firstVisual = ElementCompositionPreview.GetElementVisual(batch[0]);
        var compositor = firstVisual.Compositor;
        EnsureCompositionResources(compositor);

        var batchToken = ++_entranceBatchToken;
        var scopedBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        for (var rank = 0; rank < batch.Count; rank++)
        {
            var element = batch[rank];
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var delay = SceneListMotion.GetEntranceDelay(rank);

            // 每张卡片使用独立动画实例，避免并发修改共享动画的 DelayTime 互相覆盖。
            var translationAnimation = compositor.CreateVector3KeyFrameAnimation();
            translationAnimation.Duration = SceneListMotion.EntranceTranslationDuration;
            translationAnimation.DelayTime = delay;
            translationAnimation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            translationAnimation.InsertKeyFrame(0.0f, new Vector3(0.0f, SceneListMotion.EntranceOffsetY, 0.0f));
            translationAnimation.InsertKeyFrame(1.0f, Vector3.Zero, _entranceEasing!);

            var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
            opacityAnimation.Duration = SceneListMotion.EntranceOpacityDuration;
            opacityAnimation.DelayTime = delay;
            opacityAnimation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            opacityAnimation.InsertKeyFrame(0.0f, 0.0f);
            opacityAnimation.InsertKeyFrame(1.0f, 1.0f, _opacityEasing!);

            _activeEntranceTokens[element] = batchToken;
            visual.StartAnimation(TranslationPropertyName, translationAnimation);
            visual.StartAnimation(OpacityPropertyName, opacityAnimation);
        }

        scopedBatch.Completed += (sender, _) =>
        {
            if (sender is CompositionScopedBatch completedBatch)
            {
                completedBatch.Dispose();
            }

            DispatchToUi(() => CompleteEntranceBatch(batch, batchToken));
        };
        scopedBatch.End();
    }

    private void CompleteEntranceBatch(List<UIElement> batch, int batchToken)
    {
        if (!_attached || !_uiSettings.AnimationsEnabled)
        {
            return;
        }

        foreach (var element in batch)
        {
            if (_activeEntranceTokens.TryGetValue(element, out var token) && token == batchToken)
            {
                _activeEntranceTokens.Remove(element);
                if (_repeater.GetElementIndex(element) >= 0)
                {
                    var visual = ElementCompositionPreview.GetElementVisual(element);
                    visual.Opacity = 1f;
                    visual.Properties.InsertVector3(TranslationPropertyName, Vector3.Zero);
                }
            }
        }
    }

    private void AdvanceOffsetArming()
    {
        if (!_uiSettings.AnimationsEnabled)
        {
            _pendingOffsetArming.Clear();
            return;
        }

        var entries = _pendingOffsetArming.ToArray();
        foreach (var (element, ticksRemaining) in entries)
        {
            if (_repeater.GetElementIndex(element) < 0)
            {
                _pendingOffsetArming.Remove(element);
                continue;
            }

            if (element is FrameworkElement frameworkElement &&
                (!frameworkElement.IsLoaded || frameworkElement.ActualWidth <= 0 || frameworkElement.ActualHeight <= 0))
            {
                continue;
            }

            if (ticksRemaining <= 1)
            {
                _pendingOffsetArming.Remove(element);
                ArmElementOffsetTransition(element);
            }
            else
            {
                _pendingOffsetArming[element] = ticksRemaining - 1;
            }
        }
    }

    private void ArmElementOffsetTransition(UIElement element)
    {
        if (!_attached || !_uiSettings.AnimationsEnabled || _repeater.GetElementIndex(element) < 0)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        EnsureCompositionResources(visual.Compositor);
        visual.ImplicitAnimations = _offsetImplicitAnimations;
        _armedCards.Add(element);
    }

    private void ResetElementVisual(UIElement element)
    {
        _activeEntranceTokens.Remove(element);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.ImplicitAnimations = null;
        visual.StopAnimation(OffsetPropertyName);
        visual.StopAnimation(TranslationPropertyName);
        visual.StopAnimation(OpacityPropertyName);
        visual.Opacity = 1f;

        if (visual.Properties.TryGetVector3(TranslationPropertyName, out _) == CompositionGetValueStatus.Succeeded)
        {
            visual.Properties.InsertVector3(TranslationPropertyName, Vector3.Zero);
        }
    }

    private void EnsureCompositionResources(Compositor compositor)
    {
        if (ReferenceEquals(_compositor, compositor) &&
            _entranceEasing != null &&
            _opacityEasing != null &&
            _offsetEasing != null &&
            _offsetImplicitAnimations != null)
        {
            return;
        }

        _offsetImplicitAnimations?.Dispose();
        _entranceEasing?.Dispose();
        _opacityEasing?.Dispose();
        _offsetEasing?.Dispose();

        _compositor = compositor;
        _entranceEasing = compositor.CreateCubicBezierEasingFunction(
            SceneListMotion.EntranceControlPoint1,
            SceneListMotion.EntranceControlPoint2);
        _opacityEasing = compositor.CreateLinearEasingFunction();
        _offsetEasing = compositor.CreateCubicBezierEasingFunction(
            SceneListMotion.OffsetControlPoint1,
            SceneListMotion.OffsetControlPoint2);

        var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.Target = OffsetPropertyName;
        offsetAnimation.Duration = SceneListMotion.OffsetTransitionDuration;
        offsetAnimation.InsertExpressionKeyFrame(1.0f, "this.FinalValue", _offsetEasing);

        var implicitAnimations = compositor.CreateImplicitAnimationCollection();
        implicitAnimations[OffsetPropertyName] = offsetAnimation;
        _offsetImplicitAnimations = implicitAnimations;
    }

    private void UpdateHooks()
    {
        var needLayout = _attached && _pendingEntrance.Count > 0;
        if (needLayout != _layoutHooked)
        {
            if (needLayout)
            {
                _repeater.LayoutUpdated += OnRepeaterLayoutUpdated;
            }
            else
            {
                _repeater.LayoutUpdated -= OnRepeaterLayoutUpdated;
            }

            _layoutHooked = needLayout;
        }

        var needRendering = _attached && (_pendingEntrance.Count > 0 || _pendingOffsetArming.Count > 0);
        if (needRendering != _renderingHooked)
        {
            if (needRendering)
            {
                Windows.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
            }
            else
            {
                Windows.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
            }

            _renderingHooked = needRendering;
        }
    }

    [SupportedOSPlatform("windows10.0.19041.0")]
    private void OnAnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args) =>
        DispatchToUi(SyncAnimationsEnabled);

    private void DispatchToUi(Action action)
    {
        var dispatcher = _dispatcher;
        if (!_attached || dispatcher == null)
        {
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _ = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            if (_attached && ReferenceEquals(_dispatcher, dispatcher))
            {
                action();
            }
        });
    }

    private void SyncAnimationsEnabled()
    {
        if (!_attached)
        {
            return;
        }

        var count = _repeater.ItemsSourceView?.Count ?? 0;
        if (_uiSettings.AnimationsEnabled)
        {
            for (var index = 0; index < count; index++)
            {
                if (_repeater.TryGetElement(index) is not { } element)
                {
                    continue;
                }

                if (_pendingEntrance.Contains(element) || _pendingOffsetArming.ContainsKey(element))
                {
                    continue;
                }

                if (element is FrameworkElement { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 })
                {
                    ArmElementOffsetTransition(element);
                }
                else
                {
                    _pendingOffsetArming[element] = SceneListMotion.OffsetArmRenderTicks;
                }
            }

            UpdateHooks();
            return;
        }

        _entranceWaitingForLayout = false;
        ResetAllTrackedAndRealizedElements();
        UpdateHooks();
    }

    private void ResetAllTrackedAndRealizedElements()
    {
        var elements = new HashSet<UIElement>(_pendingEntrance);
        foreach (var element in _pendingOffsetArming.Keys)
        {
            elements.Add(element);
        }
        foreach (var element in _armedCards)
        {
            elements.Add(element);
        }
        foreach (var element in _activeEntranceTokens.Keys)
        {
            elements.Add(element);
        }

        var count = _repeater.ItemsSourceView?.Count ?? 0;
        for (var index = 0; index < count; index++)
        {
            if (_repeater.TryGetElement(index) is { } element)
            {
                elements.Add(element);
            }
        }

        foreach (var element in elements)
        {
            ResetElementVisual(element);
        }

        _pendingEntrance.Clear();
        _pendingOffsetArming.Clear();
        _armedCards.Clear();
        _activeEntranceTokens.Clear();
    }
}
