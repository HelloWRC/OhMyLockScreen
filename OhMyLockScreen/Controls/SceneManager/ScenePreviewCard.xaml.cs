using System.ComponentModel;
using OhMyLockScreen.ViewModels;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace OhMyLockScreen.Controls.SceneManager;

public sealed partial class ScenePreviewCard : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item),
        typeof(SceneCardItem),
        typeof(ScenePreviewCard),
        new PropertyMetadata(null, OnItemPropertyChanged));

    private SceneCardItem? _subscribedItem;
    private Uri? _loadedPreviewUri;
    private bool _isPointerOver;
    private bool _isPressed;
    private bool _isLoaded;

    public SceneCardItem? Item
    {
        get => (SceneCardItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        AttachItem(Item);
        SyncVisualState(useTransitions: false);
        UpdatePreviewImage();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _isPointerOver = false;
        _isPressed = false;
        DetachItem();
    }

    private static void OnItemPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (ScenePreviewCard)sender;
        if (card._isLoaded)
        {
            card.AttachItem(args.NewValue as SceneCardItem);
        }

        card.Bindings.Update();
        card.SyncVisualState(useTransitions: false);
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
            SyncVisualState(useTransitions: true);
        }

        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(SceneCardItem.PreviewImageUri))
        {
            UpdatePreviewImage();
        }
    }

    private void SyncVisualState(bool useTransitions)
    {
        var common = _isPressed ? "Pressed" : _isPointerOver ? "PointerOver" : "Normal";
        VisualStateManager.GoToState(this, common, useTransitions);
        VisualStateManager.GoToState(this, Item?.IsActive == true ? "Selected" : "Unselected", useTransitions);
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
        SyncVisualState(useTransitions: true);
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        _isPressed = false;
        SyncVisualState(useTransitions: true);
    }

    private void OnCardPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsInsideMoreButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _isPressed = true;
        SyncVisualState(useTransitions: true);
    }

    private void OnCardPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isPressed = false;
        SyncVisualState(useTransitions: true);
    }

    private void OnCardPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isPressed = false;
        SyncVisualState(useTransitions: true);
    }

    private void OnCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if (IsInsideMoreButton(e.OriginalSource as DependencyObject))
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
            || IsInsideMoreButton(e.OriginalSource as DependencyObject))
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
        GetOptionsFlyout().ShowAt(CardRoot, e.GetPosition(CardRoot));
    }

    private void OnMoreButtonTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

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
        SetActiveMenuItem.IsEnabled = Item?.CanSetActive == true;
        DeleteMenuItem.IsEnabled = Item?.CanDelete == true;
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

    private static bool IsInsideMoreButton(DependencyObject? source)
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
