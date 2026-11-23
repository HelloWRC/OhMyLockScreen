using System.Reactive.Disposables;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ComponentPresenter
{
    private Storyboard? _showCollapsed;

    private bool IsContentCollapsed(ComponentBase component) => _showCollapsed == null
        ? component.Visibility == Visibility.Collapsed
        : component.GetAnimationBaseValue(VisibilityProperty) is Visibility.Collapsed;

    // Older components can collapse their root directly. An animation reveals it without
    // replacing its binding; while held, base-value changes may not raise an effective-value callback.
    private IDisposable ShowCollapsedContentInEditor(ComponentBase component, Action publish)
    {
        void ShowIfCollapsed()
        {
            if (_showCollapsed != null || component.Visibility != Visibility.Collapsed) return;
            var animation = new ObjectAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame { KeyTime = TimeSpan.Zero, Value = Visibility.Visible });
            Storyboard.SetTarget(animation, component);
            Storyboard.SetTargetProperty(animation, nameof(Visibility));
            _showCollapsed = new Storyboard();
            _showCollapsed.Children.Add(animation);
            _showCollapsed.Begin();
            Windows.UI.Xaml.Media.CompositionTarget.Rendering += CheckBaseValue;
        }

        void Stop()
        {
            if (_showCollapsed is not { } storyboard) return;
            Windows.UI.Xaml.Media.CompositionTarget.Rendering -= CheckBaseValue;
            storyboard.Stop();
            _showCollapsed = null;
        }

        void CheckBaseValue(object? sender, object args)
        {
            if (IsContentCollapsed(component)) return;
            Stop();
            publish();
        }

        var token = component.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => ShowIfCollapsed());
        ShowIfCollapsed();
        return Disposable.Create(() =>
        {
            component.UnregisterPropertyChangedCallback(VisibilityProperty, token);
            Stop();
        });
    }
}
