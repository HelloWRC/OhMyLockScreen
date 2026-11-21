using System.Reactive.Disposables;
using System.Reactive.Linq;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Helpers;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ComponentPresenter
{
    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing), typeof(bool), typeof(ComponentPresenter),
        new PropertyMetadata(false, (sender, _) => ((ComponentPresenter)sender).UpdateContentVisibility()));

    private readonly HashSet<object> _hideRequests = new(ReferenceEqualityComparer.Instance);
    private event Action? HideRequestsChanged;
    private IDisposable? _contentVisibility;
    internal bool IsHiddenInEditor { get; private set; }

    private void SetHiddenInEditor(bool hidden)
    {
        if (IsHiddenInEditor != hidden)
        {
            IsHiddenInEditor = hidden;
            UpdateStripeMask();
        }
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
