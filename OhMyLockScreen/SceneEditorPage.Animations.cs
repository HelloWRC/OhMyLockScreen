using System.ComponentModel;
using System.Diagnostics;
using OhMyLockScreen.ViewModels;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen;

public sealed partial class SceneEditorPage
{
    private const double PanelAnimationDurationMilliseconds = 333;
    private const double PanelAnimationOffset = 350;
    private readonly UISettings _panelUiSettings = new();
    private readonly RectangleGeometry _panelClip = new();
    private PanelTransition[] _panelTransitions = [];
    private bool _panelRendering;
    private long _panelAnimationTimestamp;

    private void InitializePanelAnimations()
    {
        _panelTransitions =
        [
            new(HomePanel, HomePanelInput,
                new PanelSurface(HomePanel, HomePanelTransform, -PanelAnimationOffset)),
            new(ComponentPanels, ComponentPanels,
                new PanelSurface(ComponentLeftPanel, ComponentLeftTransform, -PanelAnimationOffset),
                new PanelSurface(ComponentRightPanel, ComponentRightTransform, PanelAnimationOffset)),
            new(BackgroundPanel, BackgroundPanelInput,
                new PanelSurface(BackgroundPanel, BackgroundPanelTransform, PanelAnimationOffset))
        ];
        MainContent.Clip = _panelClip;
    }

    private void OnPanelViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SceneEditorViewModel.ActiveTab) or nameof(SceneEditorViewModel.IsPreview))
        {
            UpdatePanelAnimations();
        }
    }

    private void UpdatePanelAnimations()
    {
        if (!_loaded)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        AdvancePanelAnimations(now);
        _panelTransitions[0].SetVisible(ViewModel.ShowHome);
        _panelTransitions[1].SetVisible(ViewModel.ShowComponents);
        _panelTransitions[2].SetVisible(ViewModel.ShowBackground);

        if (!_panelUiSettings.AnimationsEnabled)
        {
            foreach (var transition in _panelTransitions)
            {
                transition.Complete();
            }
        }

        UpdatePanelViewport();
        if (_panelTransitions.Any(transition => transition.IsAnimating))
        {
            if (!_panelRendering)
            {
                _panelRendering = true;
                _panelAnimationTimestamp = now;
                CompositionTarget.Rendering += OnPanelRendering;
            }
        }
        else
        {
            StopPanelRendering();
        }
    }

    private void OnPanelRendering(object? sender, object args)
    {
        AdvancePanelAnimations(Stopwatch.GetTimestamp());
        UpdatePanelViewport();
        if (!_panelTransitions.Any(transition => transition.IsAnimating))
        {
            StopPanelRendering();
        }
    }

    private void AdvancePanelAnimations(long now)
    {
        if (!_panelRendering)
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(_panelAnimationTimestamp, now).TotalMilliseconds;
        _panelAnimationTimestamp = now;
        var animate = _panelUiSettings.AnimationsEnabled;
        foreach (var transition in _panelTransitions)
        {
            if (animate)
            {
                transition.Advance(elapsed / PanelAnimationDurationMilliseconds);
            }
            else
            {
                transition.Complete();
            }
        }
    }

    private void UpdatePanelViewport()
    {
        _panelClip.Rect = new Rect(0, 0, MainContent.ActualWidth, MainContent.ActualHeight);
        var inset = _panelTransitions.Length == 0 ? 0 : ViewModel.SidePanelWidth * _panelTransitions[1].VisibleProgress;
        var scale = DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
        // Keep symmetric insets on whole physical pixels so centering the fixed video cannot alternate by a pixel.
        inset = Math.Round(inset * scale) / scale;
        SceneViewport.Margin = new Thickness(inset, 0, inset, 0);
    }

    private void StopPanelRendering()
    {
        CompositionTarget.Rendering -= OnPanelRendering;
        _panelRendering = false;
    }

    private void StopPanelAnimations()
    {
        StopPanelRendering();
        foreach (var transition in _panelTransitions)
        {
            transition.Reset();
        }

        UpdatePanelViewport();
    }

    private sealed record PanelSurface(FrameworkElement Element, TranslateTransform Transform, double Offset);

    private sealed class PanelTransition(FrameworkElement host, Control input, params PanelSurface[] surfaces)
    {
        private double _progress = 1;
        private double _startProgress;
        private bool _visible;

        internal bool IsAnimating => _progress < 1;

        // For cubic-bezier(0, 0, 0, 1), x = t^3 and y = 3t^2 - 2t^3.
        // Both directions ease forward from their current visual state to the new target.
        internal double VisibleProgress
        {
            get
            {
                var t = Math.Cbrt(_progress);
                var eased = t * t * (3 - 2 * t);
                return _startProgress + ((_visible ? 1 : 0) - _startProgress) * eased;
            }
        }

        internal void SetVisible(bool visible)
        {
            if (_visible != visible)
            {
                _startProgress = VisibleProgress;
                _visible = visible;
                _progress = _startProgress == (visible ? 1 : 0) ? 1 : 0;
            }

            input.IsEnabled = visible;
            host.IsHitTestVisible = visible;
            Apply();
        }

        internal void Advance(double elapsedProgress)
        {
            if (!IsAnimating)
            {
                return;
            }

            var distance = Math.Abs((_visible ? 1 : 0) - _startProgress);
            _progress = Math.Clamp(_progress + elapsedProgress / distance, 0, 1);
            Apply();
        }

        internal void Complete()
        {
            _progress = 1;
            Apply();
        }

        internal void Reset()
        {
            _visible = false;
            _startProgress = 0;
            _progress = 1;
            SetVisible(false);
        }

        private void Apply()
        {
            var progress = VisibleProgress;
            foreach (var surface in surfaces)
            {
                surface.Element.Opacity = progress;
                surface.Transform.X = surface.Offset * (1 - progress);
            }

            host.Visibility = _visible || progress > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
