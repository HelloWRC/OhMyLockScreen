using System.ComponentModel;
using System.Numerics;
using System.Runtime.Versioning;
using OhMyLockScreen.Helpers.UI;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.ViewModels;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace OhMyLockScreen.Controls.SceneManager;

public sealed partial class ScenePreviewCard : UserControl
{
    private const string TranslationPropertyName = "Translation";
    private const string OpacityPropertyName = "Opacity";

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item),
        typeof(SceneCardItem),
        typeof(ScenePreviewCard),
        new PropertyMetadata(null, OnItemPropertyChanged));

    private readonly UISettings _uiSettings = new();
    private SceneCardItem? _subscribedItem;
    private Uri? _loadedPreviewUri;
    private CoreDispatcher? _dispatcher;
    private Compositor? _compositor;
    private CompositionRoundedRectangleGeometry? _clipGeometry;
    private CompositionGeometricClip? _clip;
    private CubicBezierEasingFunction? _enterEasing;
    private CubicBezierEasingFunction? _leaveEasing;
    private bool _isPointerOver;
    private bool _isPressed;
    private bool _isFlyoutOpen;
    private bool _isLoaded;
    private bool _uiSettingsHooked;
    private int _liftAnimationToken;

    private bool IsExpanded => _isPointerOver || _isFlyoutOpen;

    public SceneCardItem? Item
    {
        get => (SceneCardItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    internal FrameworkElement CardRootElement => CardRoot;
    internal FrameworkElement HeaderOverlayElement => HeaderOverlay;
    internal FrameworkElement DescriptionElement => DescriptionTextBlock;
    internal FrameworkElement FooterOverlayElement => FooterOverlay;
    internal MaskedBackdropBlurBrush BackdropBrush => CardBackdropBrush;
    internal int CurrentLiftAnimationToken => _liftAnimationToken;
    internal int CompletedLiftAnimationToken { get; private set; }

    public event EventHandler<SceneCardItem>? Activated;
    public event EventHandler<SceneCardItem>? EditRequested;
    public event EventHandler<SceneCardItem>? PreviewRequested;
    public event EventHandler<SceneCardItem>? ExportRequested;
    public event EventHandler<SceneCardItem>? DuplicateRequested;
    public event EventHandler<SceneCardItem>? OpenFolderRequested;
    public event EventHandler<SceneCardItem>? RenameRequested;
    public event EventHandler<SceneCardItem>? DeleteRequested;

    public ScenePreviewCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    internal void SetInteractionStateForTesting(bool isPointerOver, bool isPressed = false, bool useTransitions = true)
    {
        _isPointerOver = isPointerOver;
        _isPressed = isPressed;
        SyncInteractionMotion(useTransitions);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        _dispatcher = Dispatcher;
        AttachItem(Item);
        EnsureCompositionResources();
        UpdateClipGeometrySize();

        if (!_uiSettingsHooked && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
            _uiSettingsHooked = true;
        }

        SyncSelectionState(useTransitions: false);
        SyncInteractionMotion(useTransitions: false);
        UpdatePreviewImage();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 防护 UWP 同帧重挂投递的过期 Unloaded 事件。
        if (IsLoaded)
        {
            _isLoaded = true;
            return;
        }

        if (!_isLoaded)
        {
            return;
        }

        _isLoaded = false;
        _dispatcher = null;
        _isPointerOver = false;
        _isPressed = false;
        _isFlyoutOpen = false;

        if (_uiSettingsHooked && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
            _uiSettingsHooked = false;
        }

        DetachItem();
        ResetVisualStateOnUnload();
        ReleaseCompositionResources();
    }

    private static void OnItemPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (ScenePreviewCard)sender;
        card._isPressed = false;
        if (card._isLoaded)
        {
            card.AttachItem(args.NewValue as SceneCardItem);
        }

        card.Bindings.Update();
        card.SyncSelectionState(useTransitions: false);
        if (card._isLoaded)
        {
            card.SyncInteractionMotion(useTransitions: card.IsExpanded);
        }

        card.UpdatePreviewImage();
    }

    private void AttachItem(SceneCardItem? item)
    {
        if (ReferenceEquals(_subscribedItem, item))
        {
            return;
        }

        DetachItem();
        _subscribedItem = item;
        if (item != null)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }
    }

    private void DetachItem()
    {
        if (_subscribedItem != null)
        {
            _subscribedItem.PropertyChanged -= OnItemPropertyChanged;
            _subscribedItem = null;
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(SceneCardItem.IsActive))
        {
            SyncSelectionState(useTransitions: true);
        }

        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(SceneCardItem.PreviewImageUri))
        {
            UpdatePreviewImage();
        }
    }

    private void SyncSelectionState(bool useTransitions) =>
        VisualStateManager.GoToState(this, Item?.IsActive == true ? "Selected" : "Unselected", useTransitions);

    private void EnsureCompositionResources()
    {
        // 仅动画内部 CardRoot，外层 ScenePreviewCard 的 Visual 保留给 SceneListMotionController。
        ElementCompositionPreview.SetIsTranslationEnabled(CardRoot, true);
        ElementCompositionPreview.SetIsTranslationEnabled(HeaderOverlay, true);

        var clipVisual = ElementCompositionPreview.GetElementVisual(CardClipHost);
        var compositor = clipVisual.Compositor;
        if (!ReferenceEquals(_compositor, compositor))
        {
            ReleaseCompositionResources();
            _compositor = compositor;
        }

        if (_clipGeometry == null)
        {
            _clipGeometry = compositor.CreateRoundedRectangleGeometry();
            _clipGeometry.CornerRadius = new Vector2(ScenePreviewCardMotion.CardCornerRadius);
            UpdateClipGeometrySize();
        }

        if (_clip == null)
        {
            _clip = compositor.CreateGeometricClip(_clipGeometry);
        }

        clipVisual.Clip = _clip;

        _enterEasing ??= compositor.CreateCubicBezierEasingFunction(
            ScenePreviewCardMotion.HoverEnterControlPoint1,
            ScenePreviewCardMotion.HoverEnterControlPoint2);
        _leaveEasing ??= compositor.CreateCubicBezierEasingFunction(
            ScenePreviewCardMotion.HoverLeaveControlPoint1,
            ScenePreviewCardMotion.HoverLeaveControlPoint2);
    }

    private void ReleaseCompositionResources()
    {
        ElementCompositionPreview.GetElementVisual(CardClipHost).Clip = null;
        _clip?.Dispose();
        _clip = null;
        _clipGeometry?.Dispose();
        _clipGeometry = null;
        _enterEasing?.Dispose();
        _enterEasing = null;
        _leaveEasing?.Dispose();
        _leaveEasing = null;
        _compositor = null;
    }

    private void UpdateClipGeometrySize()
    {
        if (_clipGeometry == null)
        {
            return;
        }

        var width = CardClipHost.ActualWidth > 0 ? (float)CardClipHost.ActualWidth : (float)Width;
        var height = CardClipHost.ActualHeight > 0 ? (float)CardClipHost.ActualHeight : (float)Height;
        _clipGeometry.Size = new Vector2(width, height);
    }

    private void OnCardClipHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateClipGeometrySize();
        if (_isLoaded && !IsExpanded)
        {
            SyncInteractionMotion(useTransitions: false);
        }
    }

    private void SyncInteractionMotion(bool useTransitions)
    {
        if (!_isLoaded)
        {
            return;
        }

        EnsureCompositionResources();

        var expanded = IsExpanded;
        var animate = useTransitions && _uiSettings.AnimationsEnabled;
        var cardHeight = CardClipHost.ActualHeight > 0
            ? (float)CardClipHost.ActualHeight
            : ScenePreviewCardMotion.DefaultCardHeight;

        FooterOverlay.IsHitTestVisible = expanded;
        EditButton.IsTabStop = expanded;
        PreviewButton.IsTabStop = expanded;
        MoreButton.IsTabStop = expanded;

        var duration = expanded ? ScenePreviewCardMotion.HoverEnterDuration : ScenePreviewCardMotion.HoverLeaveDuration;
        var easing = expanded ? _enterEasing! : _leaveEasing!;

        var targetMaskOffsetY = expanded
            ? ScenePreviewCardMotion.HoverMaskOffsetY
            : ScenePreviewCardMotion.GetRestMaskOffsetY(cardHeight);
        if (CardBackdropBrush.SupportsAnimatedMask || OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            if (animate)
            {
                CardBackdropBrush.AnimateMaskOffsetY(
                    targetMaskOffsetY,
                    duration,
                    expanded ? ScenePreviewCardMotion.HoverEnterControlPoint1 : ScenePreviewCardMotion.HoverLeaveControlPoint1,
                    expanded ? ScenePreviewCardMotion.HoverEnterControlPoint2 : ScenePreviewCardMotion.HoverLeaveControlPoint2);
            }
            else
            {
                CardBackdropBrush.SetMaskOffsetYImmediate(targetMaskOffsetY);
            }
        }
        else
        {
            ApplyOpacity(BackdropMaskLayer, expanded ? 1f : 0.45f, animate, duration, easing);
        }

        var targetHeaderY = expanded
            ? ScenePreviewCardMotion.HoverHeaderOffsetY
            : ScenePreviewCardMotion.GetRestHeaderOffsetY(cardHeight);
        var headerVisual = ElementCompositionPreview.GetElementVisual(HeaderOverlay);
        if (animate)
        {
            var headerAnimation = _compositor!.CreateVector3KeyFrameAnimation();
            headerAnimation.Duration = duration;
            headerAnimation.InsertKeyFrame(1.0f, new Vector3(0f, targetHeaderY, 0f), easing);
            headerVisual.StartAnimation(TranslationPropertyName, headerAnimation);
        }
        else
        {
            headerVisual.StopAnimation(TranslationPropertyName);
            headerVisual.Properties.InsertVector3(TranslationPropertyName, new Vector3(0f, targetHeaderY, 0f));
        }

        var targetDetailOpacity = expanded ? 1f : 0f;
        ApplyOpacity(DescriptionTextBlock, targetDetailOpacity, animate, duration, easing);
        ApplyOpacity(FooterOverlay, targetDetailOpacity, animate, duration, easing);

        var targetLiftY = ScenePreviewCardMotion.GetTargetLiftOffsetY(expanded, _isPressed, _uiSettings.AnimationsEnabled);
        var cardRootVisual = ElementCompositionPreview.GetElementVisual(CardRoot);
        var finalTranslation = new Vector3(0f, targetLiftY, 0f);
        var token = ++_liftAnimationToken;
        if (animate)
        {
            var batch = _compositor!.CreateScopedBatch(CompositionBatchTypes.Animation);
            var spring = _compositor.CreateSpringVector3Animation();
            spring.Target = TranslationPropertyName;
            spring.FinalValue = finalTranslation;
            spring.DampingRatio = ScenePreviewCardMotion.LiftSpringDampingRatio;
            spring.Period = ScenePreviewCardMotion.LiftSpringPeriod;
            cardRootVisual.StartAnimation(TranslationPropertyName, spring);

            var dispatcher = _dispatcher;
            batch.Completed += (sender, args) =>
            {
                if (sender is CompositionScopedBatch completedBatch)
                {
                    completedBatch.Dispose();
                }

                if (dispatcher == null)
                {
                    return;
                }

                _ = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (_isLoaded && ReferenceEquals(_dispatcher, dispatcher) && _liftAnimationToken == token)
                    {
                        var visual = ElementCompositionPreview.GetElementVisual(CardRoot);
                        visual.Properties.InsertVector3(TranslationPropertyName, finalTranslation);
                        CompletedLiftAnimationToken = token;
                    }
                });
            };
            batch.End();
        }
        else
        {
            cardRootVisual.StopAnimation(TranslationPropertyName);
            cardRootVisual.Properties.InsertVector3(TranslationPropertyName, finalTranslation);
            CompletedLiftAnimationToken = token;
        }
    }

    private void ApplyOpacity(UIElement element, float targetOpacity, bool animate, TimeSpan duration, CubicBezierEasingFunction easing)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (animate)
        {
            var animation = _compositor!.CreateScalarKeyFrameAnimation();
            animation.Duration = duration;
            animation.InsertKeyFrame(1.0f, targetOpacity, easing);
            visual.StartAnimation(OpacityPropertyName, animation);
            return;
        }

        visual.StopAnimation(OpacityPropertyName);
        visual.Opacity = targetOpacity;
    }

    private void ResetVisualStateOnUnload()
    {
        FooterOverlay.IsHitTestVisible = false;
        EditButton.IsTabStop = false;
        PreviewButton.IsTabStop = false;
        MoreButton.IsTabStop = false;

        var cardHeight = CardClipHost.ActualHeight > 0
            ? (float)CardClipHost.ActualHeight
            : ScenePreviewCardMotion.DefaultCardHeight;

        var resetToken = ++_liftAnimationToken;
        CompletedLiftAnimationToken = resetToken;

        var cardRootVisual = ElementCompositionPreview.GetElementVisual(CardRoot);
        cardRootVisual.StopAnimation(TranslationPropertyName);
        cardRootVisual.Properties.InsertVector3(TranslationPropertyName, Vector3.Zero);

        var headerVisual = ElementCompositionPreview.GetElementVisual(HeaderOverlay);
        headerVisual.StopAnimation(TranslationPropertyName);
        headerVisual.Properties.InsertVector3(
            TranslationPropertyName,
            new Vector3(0f, ScenePreviewCardMotion.GetRestHeaderOffsetY(cardHeight), 0f));

        var descriptionVisual = ElementCompositionPreview.GetElementVisual(DescriptionTextBlock);
        descriptionVisual.StopAnimation(OpacityPropertyName);
        descriptionVisual.Opacity = 0f;

        var footerVisual = ElementCompositionPreview.GetElementVisual(FooterOverlay);
        footerVisual.StopAnimation(OpacityPropertyName);
        footerVisual.Opacity = 0f;

        if (CardBackdropBrush.SupportsAnimatedMask || OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            CardBackdropBrush.SetMaskOffsetYImmediate(ScenePreviewCardMotion.GetRestMaskOffsetY(cardHeight));
        }
        else
        {
            var maskVisual = ElementCompositionPreview.GetElementVisual(BackdropMaskLayer);
            maskVisual.StopAnimation(OpacityPropertyName);
            maskVisual.Opacity = 0.45f;
        }
    }

    [SupportedOSPlatform("windows10.0.19041.0")]
    private void OnAnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        var dispatcher = _dispatcher;
        if (!_isLoaded || dispatcher == null)
        {
            return;
        }

        _ = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            if (_isLoaded && ReferenceEquals(_dispatcher, dispatcher))
            {
                SyncInteractionMotion(useTransitions: false);
            }
        });
    }

    private void UpdatePreviewImage()
    {
        var uri = Item?.PreviewImageUri;
        if (Equals(_loadedPreviewUri, uri))
        {
            return;
        }

        _loadedPreviewUri = uri;
        if (uri == null)
        {
            PreviewImageLayer.Background = null;
            return;
        }

        var bitmap = new BitmapImage(uri)
        {
            DecodePixelWidth = 712
        };
        PreviewImageLayer.Background = new ImageBrush
        {
            ImageSource = bitmap,
            Stretch = Stretch.UniformToFill
        };
    }

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = true;
        SyncInteractionMotion(useTransitions: true);
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        _isPressed = false;
        SyncInteractionMotion(useTransitions: true);
    }

    private void OnCardPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsInsideActionButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _isPressed = true;
        SyncInteractionMotion(useTransitions: true);
    }

    private void OnCardPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isPressed = false;
        SyncInteractionMotion(useTransitions: true);
    }

    private void OnCardPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isPressed = false;
        SyncInteractionMotion(useTransitions: true);
    }

    private void OnCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if (IsInsideActionButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (Item is { } item)
        {
            e.Handled = true;
            Activated?.Invoke(this, item);
        }
    }

    private void OnCardKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Space)
            || IsInsideActionButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (Item is { } item)
        {
            e.Handled = true;
            Activated?.Invoke(this, item);
        }
    }

    private void OnCardRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (Item == null)
        {
            return;
        }

        e.Handled = true;
        GetOptionsFlyout().ShowAt(InteractionRoot, e.GetPosition(InteractionRoot));
    }

    private void OnActionButtonTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void OnMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (Item == null)
        {
            return;
        }

        GetOptionsFlyout().ShowAt(MoreButton);
    }

    private MenuFlyout GetOptionsFlyout() => (MenuFlyout)Resources["CardOptionsFlyout"];

    private void OnOptionsFlyoutOpening(object sender, object e)
    {
        _isFlyoutOpen = true;
        SetActiveMenuItem.IsEnabled = Item?.CanSetActive == true;
        DeleteMenuItem.IsEnabled = Item?.CanDelete == true;
        SyncInteractionMotion(useTransitions: true);
    }

    private void OnOptionsFlyoutClosed(object sender, object e)
    {
        _isFlyoutOpen = false;
        SyncInteractionMotion(useTransitions: true);
    }

    private void OnSetActiveClick(object sender, RoutedEventArgs e) => RaiseAction(Activated);
    private void OnEditClick(object sender, RoutedEventArgs e) => RaiseAction(EditRequested);
    private void OnPreviewClick(object sender, RoutedEventArgs e) => RaiseAction(PreviewRequested);
    private void OnExportClick(object sender, RoutedEventArgs e) => RaiseAction(ExportRequested);
    private void OnDuplicateClick(object sender, RoutedEventArgs e) => RaiseAction(DuplicateRequested);
    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => RaiseAction(OpenFolderRequested);
    private void OnRenameClick(object sender, RoutedEventArgs e) => RaiseAction(RenameRequested);
    private void OnDeleteClick(object sender, RoutedEventArgs e) => RaiseAction(DeleteRequested);

    private void RaiseAction(EventHandler<SceneCardItem>? handler)
    {
        if (Item is { } item)
        {
            handler?.Invoke(this, item);
        }
    }

    private static bool IsInsideActionButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Button)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
