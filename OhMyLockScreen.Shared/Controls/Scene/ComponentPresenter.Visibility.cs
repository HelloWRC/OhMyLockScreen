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

    private IDisposable? _contentVisibility;
    private readonly HashSet<object> _hideRequests = new(ReferenceEqualityComparer.Instance);
    private event Action? HideRequestsChanged;

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
    }

    private void UpdateContentVisibility()
    {
        ClearContentVisibility();
        if (!_isLoaded || Content is not ComponentBase component)
        {
            return;
        }

        component.IsEditing = IsEditing;
        if (!IsEditing)
        {
            var changes = Observable.Create<Visibility>(observer =>
            {
                void Publish() => observer.OnNext(_hideRequests.Count > 0 ? Visibility.Collapsed : component.Visibility);
                HideRequestsChanged += Publish;
                var token = component.RegisterPropertyChangedCallback(VisibilityProperty,
                    (_, _) => Publish());
                Publish();
                return Disposable.Create(() =>
                {
                    HideRequestsChanged -= Publish;
                    component.UnregisterPropertyChangedCallback(VisibilityProperty, token);
                });
            });
            _contentVisibility = DependencyPropertyBindingHelper.Bind(changes, this, VisibilityProperty);
        }
    }
}
