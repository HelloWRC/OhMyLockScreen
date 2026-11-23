using System.Reactive.Disposables;
using System.Reactive.Linq;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers;
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
        if (!_isLoaded || Content is not ComponentBase component)
        {
            return;
        }

        component.IsEditing = IsEditing;
        var hidden = Observable.Create<bool>(observer =>
        {
            void Publish() => observer.OnNext(_hideRequests.Count > 0 || component.IsHiddenAtRuntime
                || IsContentCollapsed(component));

            var editingOverride = IsEditing ? ShowCollapsedContentInEditor(component, Publish) : null;
            HideRequestsChanged += Publish;
            var visibilityToken = component.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Publish());
            var hiddenToken = component.RegisterPropertyChangedCallback(ComponentBase.IsHiddenAtRuntimeProperty, (_, _) => Publish());
            Publish();
            return Disposable.Create(() =>
            {
                HideRequestsChanged -= Publish;
                component.UnregisterPropertyChangedCallback(VisibilityProperty, visibilityToken);
                component.UnregisterPropertyChangedCallback(ComponentBase.IsHiddenAtRuntimeProperty, hiddenToken);
                editingOverride?.Dispose();
            });
        }).DistinctUntilChanged();

        _contentVisibility = IsEditing
            ? hidden.Subscribe(SetHiddenInEditor)
            : DependencyPropertyBindingHelper.Bind(hidden.Select(value => value ? Visibility.Collapsed : Visibility.Visible),
                this, VisibilityProperty);
    }
}
