// Adapted from ClassIsland.Core.Controls.DrawerHost, GPL-3.0.
// https://github.com/ClassIsland/ClassIsland/tree/fb3ad30221d4140bae52cb058439a0668a3cfcdd
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using ProgressRing = Microsoft.UI.Xaml.Controls.ProgressRing;

namespace OhMyLockScreen.Shared.Controls.UI;

public sealed class DrawerHost : ContentControl
{
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ContentDelay = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan LoadingDelay = TimeSpan.FromMilliseconds(100);

    public enum DrawerPlacementEnum
    {
        Left,
        Right
    }

    public static readonly DependencyProperty DrawerContentProperty = DependencyProperty.Register(
        nameof(DrawerContent), typeof(object), typeof(DrawerHost), new PropertyMetadata(null, OnDrawerContentChanged));
    public static readonly DependencyProperty DrawerContentTemplateProperty = DependencyProperty.Register(
        nameof(DrawerContentTemplate), typeof(DataTemplate), typeof(DrawerHost), new PropertyMetadata(null));
    public static readonly DependencyProperty IsDrawerOpenProperty = DependencyProperty.Register(
        nameof(IsDrawerOpen), typeof(bool), typeof(DrawerHost), new PropertyMetadata(false, OnDrawerStateChanged));
    public static readonly DependencyProperty DrawerPlacementProperty = DependencyProperty.Register(
        nameof(DrawerPlacement), typeof(DrawerPlacementEnum), typeof(DrawerHost), new PropertyMetadata(DrawerPlacementEnum.Left, OnGeometryChanged));
    public static readonly DependencyProperty ActualDrawerWidthProperty = DependencyProperty.Register(
        nameof(ActualDrawerWidth), typeof(double), typeof(DrawerHost), new PropertyMetadata(0d, OnGeometryChanged));

    public object? DrawerContent { get => GetValue(DrawerContentProperty); set => SetValue(DrawerContentProperty, value); }
    public DataTemplate? DrawerContentTemplate { get => (DataTemplate?)GetValue(DrawerContentTemplateProperty); set => SetValue(DrawerContentTemplateProperty, value); }
    public bool IsDrawerOpen { get => (bool)GetValue(IsDrawerOpenProperty); set => SetValue(IsDrawerOpenProperty, value); }
    public DrawerPlacementEnum DrawerPlacement { get => (DrawerPlacementEnum)GetValue(DrawerPlacementProperty); set => SetValue(DrawerPlacementProperty, value); }
    public double ActualDrawerWidth { get => (double)GetValue(ActualDrawerWidthProperty); set => SetValue(ActualDrawerWidthProperty, value); }

    private readonly UISettings _uiSettings = new();
    private readonly RectangleGeometry _clip = new();
    private Grid? _root;
    private ContentPresenter? _main;
    private Border? _scrim;
    private Grid? _panel;
    private Border? _drawerBorder;
    private FrameworkElement? _shadowHost;
    private ContentControl? _drawer;
    private ProgressRing? _loading;
    private Storyboard? _mainMotion;
    private Storyboard? _panelMotion;
    private Storyboard? _scrimFade;
    private Storyboard? _contentFade;
    private Control? _previousFocus;
    private int _presentation;
    private bool _loaded;

    public DrawerHost()
    {
        Loaded += (_, _) => { _loaded = true; UpdatePresentation(); };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            ++_presentation;
            _previousFocus = null;
            HideLoading();
            StopAnimations();
        };
        KeyDown += OnKeyDown;
    }

    protected override void OnApplyTemplate()
    {
        ++_presentation;
        HideLoading();
        StopAnimations();
        if (_root != null) _root.SizeChanged -= OnRootSizeChanged;
        if (_drawerBorder != null) _drawerBorder.SizeChanged -= OnDrawerSizeChanged;
        if (_scrim != null) _scrim.PointerPressed -= OnOutsidePressed;
        if (_drawer != null) _drawer.Content = null;
        if (_shadowHost != null) ElementCompositionPreview.SetElementChildVisual(_shadowHost, null);

        base.OnApplyTemplate();
        _root = (Grid?)GetTemplateChild("PART_Root");
        _main = (ContentPresenter?)GetTemplateChild("PART_ContentPresenter");
        _scrim = (Border?)GetTemplateChild("PART_IgnoreLayer");
        _panel = (Grid?)GetTemplateChild("PART_Panel");
        _drawerBorder = (Border?)GetTemplateChild("PART_DrawerContentBorder");
        _shadowHost = (FrameworkElement?)GetTemplateChild("PART_Shadow");
        _drawer = (ContentControl?)GetTemplateChild("PART_DrawerContentPresenter");
        _loading = (ProgressRing?)GetTemplateChild("PART_LoadingIndicator");

        if (_root != null)
        {
            _root.Clip = _clip;
            _root.SizeChanged += OnRootSizeChanged;
        }
        if (_drawerBorder != null) _drawerBorder.SizeChanged += OnDrawerSizeChanged;
        if (_scrim != null) _scrim.PointerPressed += OnOutsidePressed;
        if (_shadowHost != null) AddShadow(_shadowHost);
        ArrangeDrawer();
        if (_loaded) UpdatePresentation();
    }

    private static void OnDrawerStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((DrawerHost)sender).UpdatePresentation();

    private static void OnDrawerContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var host = (DrawerHost)sender;
        host.ApplyWidthHint();
        if (host.IsDrawerOpen) host.UpdatePresentation();
        else if (host._panel?.Visibility == Visibility.Collapsed && host._drawer != null) host._drawer.Content = null;
    }

    private static void OnGeometryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((DrawerHost)sender).ArrangeDrawer();

    private void OnOutsidePressed(object sender, PointerRoutedEventArgs args)
    {
        args.Handled = true;
        IsDrawerOpen = false;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!args.Handled && IsDrawerOpen && args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            IsDrawerOpen = false;
        }
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args) => ArrangeDrawer();

    private void OnDrawerSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.NewSize.Width > 0 && double.IsNaN(_panel!.Width)) ActualDrawerWidth = args.NewSize.Width;
    }

    private void ApplyWidthHint()
    {
        if (DrawerContent is not FrameworkElement element) return;
        var width = double.IsNaN(element.Width) ? element.MinWidth : element.Width;
        if (width > 0) ActualDrawerWidth = width + element.Margin.Left + element.Margin.Right;
    }

    private void ArrangeDrawer()
    {
        if (_root == null || _panel == null) return;
        _clip.Rect = new Rect(0, 0, _root.ActualWidth, _root.ActualHeight);
        _panel.HorizontalAlignment = DrawerPlacement == DrawerPlacementEnum.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _panel.MaxWidth = _root.ActualWidth > 0 ? _root.ActualWidth : double.PositiveInfinity;
        if (IsDrawerOpen) MoveMain();
    }

    private float SignedWidth => (float)Math.Min(ActualDrawerWidth, _panel?.MaxWidth ?? ActualDrawerWidth) *
        (DrawerPlacement == DrawerPlacementEnum.Left ? -1 : 1);

    private void UpdatePresentation()
    {
        var presentation = ++_presentation;
        HideLoading();
        if (!_loaded || _panel == null || _scrim == null || _drawer == null || _main == null) return;

        if (!IsDrawerOpen)
        {
            _panel.IsHitTestVisible = false;
            _scrim.IsHitTestVisible = false;
            _main.IsHitTestVisible = true;
            MoveMain();
            if (_panel.Visibility == Visibility.Visible)
            {
                SetTranslation(_panel, SignedWidth, ref _panelMotion);
                SetOpacity(_scrim, 0, ref _scrimFade);
                _ = FinishClosingAsync(presentation, Dispatcher);
            }
            return;
        }

        if (_panel.Visibility != Visibility.Visible || !_panel.IsHitTestVisible)
        {
            _previousFocus ??= FocusManager.GetFocusedElement() as Control;
            ApplyWidthHint();
            SetTranslation(_panel, SignedWidth, ref _panelMotion, animate: false);
            SetOpacity(_scrim, 0, ref _scrimFade, animate: false);
        }

        _panel.Visibility = Visibility.Visible;
        _scrim.Visibility = Visibility.Visible;
        _panel.IsHitTestVisible = true;
        _scrim.IsHitTestVisible = true;
        _main.IsHitTestVisible = false;
        SetTranslation(_panel, 0, ref _panelMotion);
        SetOpacity(_scrim, 1, ref _scrimFade);
        MoveMain();
        if (Equals(_drawer.Content, DrawerContent))
        {
            SetOpacity(_drawer, 1, ref _contentFade, animate: false);
            FocusDrawer();
        }
        else
        {
            _panel.Width = ActualDrawerWidth > 0 ? ActualDrawerWidth : double.NaN;
            _drawer.Content = null;
            _ = LoadContentAsync(presentation, Dispatcher);
            _ = RevealLoadingAsync(presentation, Dispatcher);
        }
    }

    private async Task LoadContentAsync(int presentation, CoreDispatcher dispatcher)
    {
        await Task.Delay(ContentDelay).ConfigureAwait(false);
        await DispatchAsync(dispatcher, CoreDispatcherPriority.Low, () =>
        {
            if (presentation != _presentation || !IsDrawerOpen || _drawer == null || _panel == null) return;
            SetOpacity(_drawer, 0, ref _contentFade, animate: false);
            _drawer.Content = DrawerContent;
            _panel.Width = double.NaN;
            HideLoading();
            SetOpacity(_drawer, 1, ref _contentFade);
            FocusDrawer();
        });
    }

    private async Task RevealLoadingAsync(int presentation, CoreDispatcher dispatcher)
    {
        await Task.Delay(LoadingDelay).ConfigureAwait(false);
        await DispatchAsync(dispatcher, CoreDispatcherPriority.Normal, () =>
        {
            if (presentation != _presentation || !IsDrawerOpen || _loading == null || Equals(_drawer?.Content, DrawerContent)) return;
            _loading.Visibility = Visibility.Visible;
            _loading.IsActive = true;
        });
    }

    private async Task FinishClosingAsync(int presentation, CoreDispatcher dispatcher)
    {
        if (_uiSettings.AnimationsEnabled) await Task.Delay(AnimationDuration).ConfigureAwait(false);
        await DispatchAsync(dispatcher, CoreDispatcherPriority.Normal, () =>
        {
            if (presentation != _presentation || IsDrawerOpen || _panel == null || _scrim == null || _drawer == null) return;
            _panel.Visibility = Visibility.Collapsed;
            _scrim.Visibility = Visibility.Collapsed;
            if (DrawerContent == null) _drawer.Content = null;
            _previousFocus?.Focus(FocusState.Programmatic);
            _previousFocus = null;
        });
    }

    private void FocusDrawer()
    {
        var first = _drawer != null ? FocusManager.FindFirstFocusableElement(_drawer) as Control : null;
        (first ?? this).Focus(FocusState.Programmatic);
    }

    private void HideLoading()
    {
        if (_loading == null) return;
        _loading.IsActive = false;
        _loading.Visibility = Visibility.Collapsed;
    }

    private void MoveMain()
    {
        if (_main != null) SetTranslation(_main, IsDrawerOpen && _uiSettings.AnimationsEnabled ? -SignedWidth * 0.07325f : 0, ref _mainMotion);
    }

    private void SetTranslation(UIElement element, double x, ref Storyboard? running, bool animate = true)
    {
        if (element.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }
        var previous = transform.X;
        running?.Stop();
        transform.X = x;
        running = Animate(transform, "X", previous, x, animate);
    }

    private void SetOpacity(UIElement element, double opacity, ref Storyboard? running, bool animate = true)
    {
        var previous = element.Opacity;
        running?.Stop();
        element.Opacity = opacity;
        running = Animate(element, "Opacity", previous, opacity, animate);
    }

    private Storyboard? Animate(DependencyObject target, string property, double from, double to, bool animate)
    {
        if (!animate || !_uiSettings.AnimationsEnabled || from == to) return null;
        var animation = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = true, FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { Value = from, KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero) });
        animation.KeyFrames.Add(new SplineDoubleKeyFrame { Value = to, KeyTime = KeyTime.FromTimeSpan(AnimationDuration),
            KeySpline = new KeySpline { ControlPoint1 = new Point(0, 0), ControlPoint2 = new Point(0, 1) } });
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
        return storyboard;
    }

    private void StopAnimations()
    {
        _mainMotion?.Stop();
        _panelMotion?.Stop();
        _scrimFade?.Stop();
        _contentFade?.Stop();
        _mainMotion = _panelMotion = _scrimFade = _contentFade = null;
    }

    private static void AddShadow(FrameworkElement host)
    {
        var visual = ElementCompositionPreview.GetElementVisual(host);
        var compositor = visual.Compositor;
        var sprite = compositor.CreateSpriteVisual();
        var shadow = compositor.CreateDropShadow();
        shadow.BlurRadius = 8;
        shadow.Opacity = 0.2f;
        shadow.Color = Colors.Black;
        sprite.Shadow = shadow;
        var size = compositor.CreateExpressionAnimation("host.Size");
        size.SetReferenceParameter("host", visual);
        sprite.StartAnimation(nameof(Visual.Size), size);
        ElementCompositionPreview.SetElementChildVisual(host, sprite);
    }

    private static async Task DispatchAsync(CoreDispatcher dispatcher, CoreDispatcherPriority priority, Action update)
    {
        try { await dispatcher.RunAsync(priority, () => update()); }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine($"无法更新设置抽屉：{exception}"); }
    }
}
