using System.Numerics;
using System.Reactive.Disposables;
using Microsoft.Graphics.Canvas.Effects;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ComponentPresenter
{
    private IDisposable? _stripeMask;

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

    private void UpdateStripeMask()
    {
        _stripeMask?.Dispose();
        _stripeMask = null;
        if (!IsHiddenInEditor || Border is not { } source
            || VisualTreeHelper.GetParent(source) is not FrameworkElement original
            || VisualTreeHelper.GetParent(original) is not FrameworkElement host)
        {
            return;
        }

        var sourceVisual = ElementCompositionPreview.GetElementVisual(source);
        var compositor = sourceVisual.Compositor;
        var resources = new CompositeDisposable();
        try
        {
            var stripes = compositor.CreateLinearGradientBrush();
            resources.Add(stripes);
            stripes.MappingMode = CompositionMappingMode.Absolute;
            stripes.ExtendMode = CompositionGradientExtendMode.Wrap;
            stripes.EndPoint = new Vector2(10 / MathF.Sqrt(2));
            AddStop(0, 0xAF);
            AddStop(0.5f, 0xAF);
            AddStop(0.5f, 0x50);
            AddStop(1, 0x50);

            using var mask = new AlphaMaskEffect
            {
                Source = new CompositionEffectSourceParameter("Content"),
                AlphaMask = new CompositionEffectSourceParameter("Stripes")
            };
            var factory = compositor.CreateEffectFactory(mask);
            resources.Add(factory);
            var brush = factory.CreateBrush();
            resources.Add(brush);
            // The unbound Content input is the live subtree of the layer.
            brush.SetSourceParameter("Stripes", stripes);
            var layer = compositor.CreateLayerVisual();
            resources.Add(layer);
            layer.RelativeSizeAdjustment = Vector2.One;
            layer.Effect = brush;
            var redirect = compositor.CreateRedirectVisual(sourceVisual);
            resources.Add(redirect);
            redirect.RelativeSizeAdjustment = Vector2.One;
            layer.Children.InsertAtTop(redirect);
            ElementCompositionPreview.SetElementChildVisual(host, layer);
            // XAML Opacity=0 culls the source subtree. Hide only its ancestor in Composition.
            var originalVisual = ElementCompositionPreview.GetElementVisual(original);
            originalVisual.Opacity = 0;
            _stripeMask = Disposable.Create(() =>
            {
                originalVisual.Opacity = 1;
                ElementCompositionPreview.SetElementChildVisual(host, null);
                resources.Dispose();
            });

            void AddStop(float offset, byte alpha)
            {
                var stop = compositor.CreateColorGradientStop(offset, Color.FromArgb(alpha, 255, 255, 255));
                resources.Add(stop);
                stripes.ColorStops.Add(stop);
            }
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }
}
