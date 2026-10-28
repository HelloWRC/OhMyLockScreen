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
                var token = component.RegisterPropertyChangedCallback(VisibilityProperty,
                    (_, _) => observer.OnNext(component.Visibility));
                observer.OnNext(component.Visibility);
                return Disposable.Create(() => component.UnregisterPropertyChangedCallback(VisibilityProperty, token));
            });
            _contentVisibility = DependencyPropertyBindingHelper.Bind(changes, this, VisibilityProperty);
        }
    }
}
