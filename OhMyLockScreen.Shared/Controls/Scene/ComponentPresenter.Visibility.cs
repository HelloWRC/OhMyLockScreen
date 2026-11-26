using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ComponentPresenter
{
    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing), typeof(bool), typeof(ComponentPresenter),
        new PropertyMetadata(false, (sender, _) => ((ComponentPresenter)sender).OnIsEditingChanged()));

    private readonly HashSet<object> _hideRequests = new(ReferenceEqualityComparer.Instance);
    private event Action? HideRequestsChanged;
    private IDisposable? _contentVisibility;
    private StripeMaskDecorator? _stripeMask;
    private IDecoratorRegistration? _stripeMaskRegistration;
    internal bool IsHiddenInEditor { get; private set; }

    private void OnIsEditingChanged()
    {
        UpdateStripeMask();
        UpdateContentVisibility();
    }

    private void SetHiddenInEditor(bool hidden)
    {
        if (IsHiddenInEditor != hidden)
        {
            IsHiddenInEditor = hidden;
            UpdateStripeMask();
        }
    }

    private void UpdateStripeMask()
    {
        if (!_isLoaded || !IsEditing)
        {
            _stripeMaskRegistration?.Dispose();
            _stripeMaskRegistration = null;
            _stripeMask = null;
            return;
        }

        if (_stripeMask == null)
        {
            _stripeMask = new StripeMaskDecorator();
            _stripeMaskRegistration = Decorators.Register(_stripeMask, DecoratorPriorities.EditorOverlay);
        }

        _stripeMask.IsMaskActive = IsHiddenInEditor;
    }

    public void SetVisibilitySuppressed(object owner, bool suppressed)
    {
        if (suppressed ? _hideRequests.Add(owner) : _hideRequests.Remove(owner)) HideRequestsChanged?.Invoke();
    }

    public bool IsEditing
    {
        get => GetValue(IsEditingProperty) is true;
        set => SetValue(IsEditingProperty, value);
    }

    private void ClearContentVisibility()
    {
        _contentVisibility?.Dispose();
        _contentVisibility = null;
        SetHiddenInEditor(false);
    }

    private void UpdateContentVisibility()
    {
        ClearContentVisibility();
        if (!_isLoaded)
        {
            return;
        }

        var component = Content as ComponentBase;
        var settings = Settings;
        if (component != null)
        {
            component.IsEditing = IsEditing;
        }

        if (component == null && settings == null)
        {
            return;
        }

        var state = Observable.Create<(bool ManuallyHidden, bool RuntimeHidden)>(observer =>
        {
            var disposed = false;
            var syncingOverride = false;
            IDisposable? editingOverride = null;

            void SyncEditingOverride(bool manuallyHidden)
            {
                if (syncingOverride)
                {
                    return;
                }

                syncingOverride = true;
                try
                {
                    if (IsEditing && component != null && !manuallyHidden)
                    {
                        editingOverride ??= ShowCollapsedContentInEditor(component, Publish);
                    }
                    else if (editingOverride != null)
                    {
                        editingOverride.Dispose();
                        editingOverride = null;
                    }
                }
                finally
                {
                    syncingOverride = false;
                }
            }

            void Publish()
            {
                if (disposed || syncingOverride)
                {
                    return;
                }

                // 场景树显隐优先级高于规则集与组件自身运行时隐藏意图，在编辑器与运行态均隐藏节点。
                var manuallyHidden = settings is { IsVisible: false };
                SyncEditingOverride(manuallyHidden);
                var runtimeHidden = _hideRequests.Count > 0
                    || (component != null && (component.IsHiddenAtRuntime || IsContentCollapsed(component)));
                observer.OnNext((manuallyHidden, runtimeHidden));
            }

            void OnSettingsVisibilityChanged(object? sender, PropertyChangedEventArgs args)
            {
                if (!string.IsNullOrEmpty(args.PropertyName) && args.PropertyName != nameof(ComponentSettings.IsVisible))
                {
                    return;
                }

                if (Dispatcher.HasThreadAccess)
                {
                    Publish();
                }
                else
                {
                    _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        if (!disposed && _isLoaded && ReferenceEquals(Settings, settings))
                        {
                            Publish();
                        }
                    });
                }
            }

            if (settings != null)
            {
                settings.PropertyChanged += OnSettingsVisibilityChanged;
            }

            HideRequestsChanged += Publish;
            var visibilityToken = component?.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Publish()) ?? 0;
            var hiddenToken = component?.RegisterPropertyChangedCallback(ComponentBase.IsHiddenAtRuntimeProperty, (_, _) => Publish()) ?? 0;
            Publish();
            return Disposable.Create(() =>
            {
                disposed = true;
                if (settings != null)
                {
                    settings.PropertyChanged -= OnSettingsVisibilityChanged;
                }

                HideRequestsChanged -= Publish;
                if (component != null)
                {
                    component.UnregisterPropertyChangedCallback(VisibilityProperty, visibilityToken);
                    component.UnregisterPropertyChangedCallback(ComponentBase.IsHiddenAtRuntimeProperty, hiddenToken);
                }

                editingOverride?.Dispose();
                editingOverride = null;
            });
        }).DistinctUntilChanged().Publish();

        var subscriptions = new CompositeDisposable();
        subscriptions.Add(DependencyPropertyBindingHelper.Bind(
            state.Select(value => value.ManuallyHidden || (!IsEditing && value.RuntimeHidden)
                ? Visibility.Collapsed
                : Visibility.Visible),
            this, VisibilityProperty));
        if (IsEditing)
        {
            subscriptions.Add(state.Subscribe(value => SetHiddenInEditor(!value.ManuallyHidden && value.RuntimeHidden)));
        }

        subscriptions.Add(state.Connect());
        _contentVisibility = subscriptions;
    }
}
