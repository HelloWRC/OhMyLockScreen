using System.Reflection;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Globalization.NumberFormatting;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using OhMyLockScreen.Shared.Helpers.UI;
using Muxc = Microsoft.UI.Xaml.Controls;
using RadialGradientBrush = Microsoft.UI.Xaml.Media.RadialGradientBrush;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>
/// Edits static UWP brushes. Bind Brush with Mode=TwoWay; edits assign a new instance.
/// TargetElement supplies only the painted bounds, and never has its brush changed directly.
/// </summary>
public sealed partial class BrushEditControl : UserControl
{
    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(BrushEditControl), new PropertyMetadata(null, OnBrushChanged));

    public static readonly DependencyProperty TargetElementProperty = DependencyProperty.Register(
        nameof(TargetElement), typeof(FrameworkElement), typeof(BrushEditControl), new PropertyMetadata(null, OnTargetChanged));

    public Brush? Brush
    {
        get => (Brush?)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public FrameworkElement? TargetElement
    {
        get => (FrameworkElement?)GetValue(TargetElementProperty);
        set => SetValue(TargetElementProperty, value);
    }

    private readonly DispatcherTimer _collectionTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly List<(DependencyObject Source, DependencyProperty Property, long Token)> _subscriptions = [];
    private GradientStop[] _observedStops = [];
    private BitmapImage? _observedImage;
    private BrushGradientAdorner? _adorner;
    private bool _ready;
    private bool _loaded;
    private bool _synchronizing;
    private bool _publishing;
    private bool _selectionReady;
    private int _selectedStop = -1;
    private Button? _dragHandle;
    private int _dragStop = -1;
    private uint _dragPointer;
    private double _dragGrabOffset;
    private long _revision;
    private long _browseRequest;

    public BrushEditControl()
    {
        InitializeComponent();
        // NumberBox supplies a local default formatter, so a style setter cannot replace it.
        var formatter = (Windows.Globalization.NumberFormatting.PercentFormatter)Resources["PercentageFormatter"];
        OffsetInput.NumberFormatter = new PercentFormatter
        {
            FractionDigits = 1,
            NumberRounder = new IncrementNumberRounder
            {
                // PercentFormatter rounds the underlying ratio before multiplying by 100.
                Increment = 0.001,
                RoundingAlgorithm = RoundingAlgorithm.RoundHalfAwayFromZero
            }
        };
        OpacityInput.NumberFormatter = formatter;
        TintOpacityInput.NumberFormatter = formatter;
        _ready = true;
        _collectionTimer.Tick += OnCollectionTick;
        Sync();
    }

    private static void OnBrushChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var editor = (BrushEditControl)sender;
        editor._revision++;
        if (!editor._publishing)
        {
            editor.CancelStripDrag();
            editor._adorner?.CancelDrag();
            editor._selectedStop = -1;
        }
        editor.Observe();
        editor.Sync();
    }

    private static void OnTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var editor = (BrushEditControl)sender;
        editor._adorner?.Dispose();
        editor._adorner = null;
        editor.UpdateAdorner();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        _selectionReady = false;
        BrowseButton.IsEnabled = true;
        Observe();
        Sync();
        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
        {
            if (_loaded)
            {
                Sync();
                _selectionReady = true;
            }
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        _selectionReady = false;
        _browseRequest++;
        _collectionTimer.Stop();
        CancelStripDrag();
        Unobserve();
        _adorner?.Dispose();
        _adorner = null;
    }

    private void OnFocusChanged(object sender, RoutedEventArgs args)
    {
        // Focus is not finalized until the current routed event has finished.
        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, UpdateAdorner);
    }

    private void OnCollectionTick(object? sender, object args)
    {
        var stops = BrushEditorOperations.Stops(Brush);
        if (stops != null && !_observedStops.SequenceEqual(stops))
        {
            CancelStripDrag();
            _adorner?.CancelDrag();
            var selected = _selectedStop >= 0 && _selectedStop < _observedStops.Length ? _observedStops[_selectedStop] : null;
            if (selected != null && stops.IndexOf(selected) is var index && index >= 0)
            {
                _selectedStop = index;
            }
            Observe();
            Sync();
        }
        UpdateAdorner();
    }

    private void Observe()
    {
        Unobserve();
        if (!_loaded || Brush == null || BrushEditorOperations.Kind(Brush) == BrushEditorKind.Unsupported)
        {
            return;
        }
        var seen = new HashSet<DependencyObject>();
        void Watch(DependencyObject? source)
        {
            if (source == null || !seen.Add(source))
            {
                return;
            }
            foreach (var property in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (source is AcrylicBrush && property.Name == "TintLuminosityOpacityProperty"
                    && !ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Media.AcrylicBrush", "TintLuminosityOpacity"))
                {
                    continue;
                }
                if (property.PropertyType == typeof(DependencyProperty) && property.GetValue(null) is DependencyProperty identifier)
                {
                    var token = source.RegisterPropertyChangedCallback(identifier, OnObservedPropertyChanged);
                    _subscriptions.Add((source, identifier, token));
                }
            }
            if (source is TransformGroup group)
            {
                foreach (var child in group.Children)
                {
                    Watch(child);
                }
            }
        }
        Watch(Brush);
        Watch(Brush.Transform);
        Watch(Brush.RelativeTransform);
        if (BrushEditorOperations.Stops(Brush) is { } stops)
        {
            _observedStops = stops.ToArray();
            foreach (var stop in stops)
            {
                Watch(stop);
            }
        }
        if (Brush is ImageBrush image)
        {
            Watch(image.ImageSource);
            if (image.ImageSource is BitmapImage bitmap)
            {
                _observedImage = bitmap;
                bitmap.ImageOpened += OnImageOpened;
                bitmap.ImageFailed += OnImageFailed;
            }
        }
    }

    private void Unobserve()
    {
        foreach (var (source, property, token) in _subscriptions)
        {
            source.UnregisterPropertyChangedCallback(property, token);
        }
        _subscriptions.Clear();
        _observedStops = [];
        if (_observedImage != null)
        {
            _observedImage.ImageOpened -= OnImageOpened;
            _observedImage.ImageFailed -= OnImageFailed;
            _observedImage = null;
        }
    }

    private void OnObservedPropertyChanged(DependencyObject sender, DependencyProperty property)
    {
        _revision++;
        Observe();
        Sync();
    }

    private void OnImageOpened(object sender, RoutedEventArgs args) => Sync();

    private void OnImageFailed(object sender, ExceptionRoutedEventArgs args)
    {
        EmptyImageText.Text = "无法显示图片";
        EmptyImageText.Visibility = Visibility.Visible;
        ShowError(new InvalidDataException(args.ErrorMessage));
    }

    private void Sync()
    {
        if (!_ready || _synchronizing)
        {
            return;
        }
        _synchronizing = true;
        try
        {
            var kind = BrushEditorOperations.Kind(Brush);
            TypeSelector.SelectedIndex = (int)kind;
            var supported = kind is not BrushEditorKind.None and not BrushEditorKind.Unsupported;
            var gradient = kind is BrushEditorKind.Linear or BrushEditorKind.Radial;
            if (_loaded && gradient)
            {
                _collectionTimer.Start();
            }
            else
            {
                _collectionTimer.Stop();
            }
            Picker.Visibility = Visible(kind is BrushEditorKind.Solid or BrushEditorKind.Acrylic || gradient);
            GradientPanel.Visibility = Visible(gradient);
            ImagePanel.Visibility = Visible(kind == BrushEditorKind.Image);
            AcrylicColorSelector.Visibility = FallbackCheckBox.Visibility = TintOpacityRow.Visibility = Visible(kind == BrushEditorKind.Acrylic);
            OpacityRow.Visibility = Visible(supported);
            ErrorText.Visibility = Visible(kind == BrushEditorKind.Unsupported);
            ErrorText.Text = kind == BrushEditorKind.Unsupported ? $"不支持编辑 {Brush!.GetType().Name}，请选择其他笔刷类型。" : "";
            OpacityInput.Value = Brush?.Opacity ?? 1;
            var stops = BrushEditorOperations.Stops(Brush);
            _selectedStop = stops?.Count > 0 ? Math.Clamp(_selectedStop, 0, stops.Count - 1) : -1;
            OffsetInput.IsEnabled = _selectedStop >= 0;
            OffsetInput.Value = _selectedStop >= 0 ? stops![_selectedStop].Offset : 0;
            Picker.IsEnabled = !gradient || _selectedStop >= 0;
            Picker.Color = Brush switch
            {
                SolidColorBrush solid => solid.Color,
                AcrylicBrush acrylic => AcrylicColorSelector.SelectedIndex == 1 ? acrylic.FallbackColor : acrylic.TintColor,
                _ => _selectedStop >= 0 ? stops![_selectedStop].Color : BrushEditorOperations.White
            };
            if (Brush is AcrylicBrush value)
            {
                TintOpacityInput.Value = value.TintOpacity;
                FallbackCheckBox.IsChecked = value.AlwaysUseFallback;
            }
            if (Brush is ImageBrush image)
            {
                ImagePreviewSurface.Fill = image;
                EmptyImageText.Text = "请选择图片";
                EmptyImageText.Visibility = Visible(image.ImageSource == null);
                StretchSelector.SelectedIndex = (int)image.Stretch;
            }
            else
            {
                ImagePreviewSurface.Fill = null;
            }
            foreach (var child in EditorPanel.Children.OfType<FrameworkElement>().Where(child => child != TypeSelector))
            {
                // UWP StackPanel.Spacing also leaves gaps for collapsed sections.
                child.Margin = child.Visibility == Visibility.Visible ? new Thickness(0, 8, 0, 0) : new Thickness();
            }
            UpdateStopStrip();
            UpdateAdorner();
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private void Publish(Brush? value)
    {
        _publishing = true;
        try
        {
            Brush = value;
        }
        finally
        {
            _publishing = false;
        }
    }

    internal void Edit(Action<Brush> change)
    {
        if (_synchronizing || Brush == null || BrushEditorOperations.Kind(Brush) == BrushEditorKind.Unsupported)
        {
            return;
        }
        try
        {
            Publish(BrushEditorOperations.Edit(Brush, change));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            ShowError(exception);
        }
    }

    private void OnTypeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_synchronizing || !_ready || !_selectionReady || TypeSelector.SelectedIndex < 0
            || (int)BrushEditorOperations.Kind(Brush) == TypeSelector.SelectedIndex)
        {
            return;
        }
        CancelStripDrag();
        _adorner?.CancelDrag();
        _selectedStop = -1;
        Publish(BrushEditorOperations.Create((BrushEditorKind)TypeSelector.SelectedIndex, Brush?.Opacity ?? 1));
    }

    private void OnColorChanged(Muxc.ColorPicker sender, Muxc.ColorChangedEventArgs args) => Edit(copy =>
    {
        switch (copy)
        {
            case SolidColorBrush solid:
                solid.Color = args.NewColor;
                break;
            case AcrylicBrush acrylic when AcrylicColorSelector.SelectedIndex == 1:
                acrylic.FallbackColor = args.NewColor;
                break;
            case AcrylicBrush acrylic:
                acrylic.TintColor = args.NewColor;
                break;
            default:
                if (BrushEditorOperations.Stops(copy) is { } stops && _selectedStop >= 0 && _selectedStop < stops.Count)
                {
                    stops[_selectedStop].Color = args.NewColor;
                }
                break;
        }
    });

    private void OnAcrylicColorChanged(object sender, SelectionChangedEventArgs args) => Sync();
    private void OnOpacityChanged(Muxc.NumberBox sender, Muxc.NumberBoxValueChangedEventArgs args)
    {
        if (double.IsFinite(args.NewValue))
        {
            Edit(copy => copy.Opacity = Math.Clamp(args.NewValue, 0, 1));
        }
    }

    private void OnTintOpacityChanged(Muxc.NumberBox sender, Muxc.NumberBoxValueChangedEventArgs args)
    {
        if (Brush is AcrylicBrush && double.IsFinite(args.NewValue))
        {
            Edit(copy => ((AcrylicBrush)copy).TintOpacity = Math.Clamp(args.NewValue, 0, 1));
        }
    }

    private void OnFallbackChanged(object sender, RoutedEventArgs args)
    {
        if (Brush is AcrylicBrush)
        {
            Edit(copy => ((AcrylicBrush)copy).AlwaysUseFallback = FallbackCheckBox.IsChecked == true);
        }
    }
    private void OnStretchChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_ready && Brush is ImageBrush && StretchSelector.SelectedIndex >= 0)
        {
            Edit(copy => ((ImageBrush)copy).Stretch = (Stretch)StretchSelector.SelectedIndex);
        }
    }

    private void OnOffsetChanged(Muxc.NumberBox sender, Muxc.NumberBoxValueChangedEventArgs args)
    {
        if (!_synchronizing && double.IsFinite(args.NewValue))
        {
            SetStopOffset(_selectedStop, args.NewValue);
        }
    }

    internal int SelectedStop => _selectedStop;
    internal void SelectStop(int index)
    {
        _selectedStop = index;
        Sync();
    }

    internal void SetStopOffset(int index, double offset)
    {
        if (BrushEditorOperations.Stops(Brush) is { } stops && index >= 0 && index < stops.Count)
        {
            _selectedStop = index;
            Publish(BrushEditorOperations.SetOffset(Brush!, index, offset));
        }
    }

    internal void AddStop(double offset)
    {
        if (BrushEditorOperations.Stops(Brush) is { } stops)
        {
            _selectedStop = stops.Count;
            Publish(BrushEditorOperations.AddStop(Brush!, offset));
        }
    }

    internal void DeleteStop(int index)
    {
        if (BrushEditorOperations.Stops(Brush) is { } stops && index >= 0 && index < stops.Count)
        {
            CancelStripDrag();
            _adorner?.CancelDrag();
            _selectedStop = Math.Min(index, stops.Count - 2);
            Publish(BrushEditorOperations.RemoveStop(Brush!, index));
        }
    }

    private void OnReverseClick(object sender, RoutedEventArgs args)
    {
        if (BrushEditorOperations.Stops(Brush) != null)
        {
            Publish(BrushEditorOperations.Reverse(Brush!));
        }
    }

    private void OnStopStripSizeChanged(object sender, SizeChangedEventArgs args) => UpdateStopStrip();
    private void OnImagePreviewSizeChanged(object sender, SizeChangedEventArgs args) => ImagePreview.Height = args.NewSize.Width * 9 / 16;

    private bool IsStopHandleSource(object source)
    {
        for (var element = source as DependencyObject; element != null && element != StopStrip; element = VisualTreeHelper.GetParent(element))
        {
            if (element is Button)
            {
                return true;
            }
        }
        return false;
    }

    private void OnStopStripTapped(object sender, TappedRoutedEventArgs args)
    {
        if (IsStopHandleSource(args.OriginalSource) || StopStrip.ActualWidth <= 20
            || BrushEditorOperations.Stops(Brush) is not { Count: > 0 } stops)
        {
            return;
        }
        var offset = (args.GetPosition(StopStrip).X - 10) / (StopStrip.ActualWidth - 20);
        var index = Enumerable.Range(0, stops.Count).MinBy(index => Math.Abs(stops[index].Offset - offset));
        SelectStop(index);
        ((Button)StopHandles.Children[index]).Focus(FocusState.Pointer);
        args.Handled = true;
    }

    private void OnStopStripDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (IsStopHandleSource(args.OriginalSource))
        {
            return;
        }
        if (StopStrip.ActualWidth > 20)
        {
            AddStop((args.GetPosition(StopStrip).X - 10) / (StopStrip.ActualWidth - 20));
            args.Handled = true;
        }
    }

    private void UpdateStopStrip()
    {
        if (!_ready)
        {
            return;
        }
        var stops = BrushEditorOperations.Stops(Brush);
        var preview = new LinearGradientBrush { StartPoint = new(0, 0.5), EndPoint = new(1, 0.5) };
        foreach (var stop in stops ?? [])
        {
            preview.GradientStops.Add(new GradientStop { Color = stop.Color, Offset = stop.Offset });
        }
        StopTrack.Background = preview;
        // Keep the captured button alive while publishing replacement brushes during a drag.
        while (StopHandles.Children.Count > (stops?.Count ?? 0))
        {
            StopHandles.Children.RemoveAt(StopHandles.Children.Count - 1);
        }
        for (var index = 0; index < (stops?.Count ?? 0); index++)
        {
            if (index >= StopHandles.Children.Count)
            {
                var button = CreateHandle(OnStopPressed, OnStopMoved, OnStopReleased, OnStopCanceled, OnStopKeyDown);
                button.Click += OnStopClick;
                button.GotFocus += OnStopGotFocus;
                StopHandles.Children.Add(button);
            }
            var handle = (Button)StopHandles.Children[index];
            handle.Tag = index;
            PaintHandle(handle, stops![index].Color, index == _selectedStop);
            Canvas.SetLeft(handle, Math.Clamp(stops[index].Offset, 0, 1) * Math.Max(0, StopStrip.ActualWidth - 20) - 4);
            Canvas.SetTop(handle, 2);
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(handle, $"色标 {index + 1}，位置 {stops[index].Offset:P1}");
            ToolTipService.SetToolTip(handle, $"{stops[index].Offset:P1}；Delete 删除；方向键移动");
        }
    }

    internal Button CreateHandle(
        PointerEventHandler pressed,
        PointerEventHandler moved,
        PointerEventHandler released,
        PointerEventHandler canceled,
        KeyEventHandler keyDown)
    {
        var handle = new Button
        {
            Width = 28,
            Height = 28,
            Style = (Style)Resources["BrushEditorHandleStyle"],
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            ManipulationMode = ManipulationModes.None
        };
        // ButtonBase consumes pointer events and captures the pointer itself. Listen
        // to handled events so dragging works without replacing its accessible button behavior.
        handle.AddHandler(PointerPressedEvent, pressed, true);
        handle.AddHandler(PointerMovedEvent, moved, true);
        handle.AddHandler(PointerReleasedEvent, released, true);
        handle.AddHandler(PointerCanceledEvent, canceled, true);
        handle.AddHandler(PointerCaptureLostEvent, canceled, true);
        handle.AddHandler(KeyDownEvent, keyDown, true);
        return handle;
    }

    internal static bool IsPrimaryHandlePress(Button handle, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(handle);
        return args.Pointer.PointerDeviceType == PointerDeviceType.Mouse
            ? point.Properties.IsLeftButtonPressed : point.IsInContact;
    }

    internal static bool CaptureHandlePointer(Button handle, PointerRoutedEventArgs args) =>
        handle.PointerCaptures?.Any(pointer => pointer.PointerId == args.Pointer.PointerId) == true
        || handle.CapturePointer(args.Pointer);

    internal static void PaintHandle(Button handle, Color color, bool selected)
    {
        handle.Background = new SolidColorBrush(color);
        handle.BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(255, 96, 205, 255) : Color.FromArgb(255, 128, 128, 128));
    }

    private void OnStopPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_dragHandle == null && sender is Button { Tag: int index } handle && IsPrimaryHandlePress(handle, args))
        {
            SelectStop(index);
            handle.Focus(FocusState.Pointer);
            if (CaptureHandlePointer(handle, args))
            {
                _dragHandle = handle;
                _dragStop = index;
                _dragPointer = args.Pointer.PointerId;
                var offset = BrushEditorOperations.Stops(Brush)![index].Offset;
                _dragGrabOffset = args.GetCurrentPoint(StopStrip).Position.X - (10 + offset * (StopStrip.ActualWidth - 20));
            }
            args.Handled = true;
        }
    }

    private void OnStopMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_dragHandle != null && args.Pointer.PointerId == _dragPointer && StopStrip.ActualWidth > 20)
        {
            var offset = (args.GetCurrentPoint(StopStrip).Position.X - _dragGrabOffset - 10) / (StopStrip.ActualWidth - 20);
            SetStopOffset(_dragStop, BrushEditorOperations.RoundDraggedOffset(offset));
            args.Handled = true;
        }
    }

    private void OnStopReleased(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(sender, _dragHandle) && args.Pointer.PointerId == _dragPointer)
        {
            CancelStripDrag();
            args.Handled = true;
        }
    }

    private void OnStopCanceled(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(sender, _dragHandle) && args.Pointer.PointerId == _dragPointer)
        {
            CancelStripDrag();
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: int index })
        {
            SelectStop(index);
        }
    }

    private void OnStopGotFocus(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: int index } && _selectedStop != index)
        {
            SelectStop(index);
        }
    }

    private void CancelStripDrag()
    {
        var handle = _dragHandle;
        _dragHandle = null;
        _dragStop = -1;
        handle?.ReleasePointerCaptures();
    }

    internal void HandleStopKey(int index, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Delete)
        {
            DeleteStop(index);
            args.Handled = true;
        }
        else if (BrushEditorOperations.Stops(Brush) is { } stops && index >= 0 && index < stops.Count)
        {
            var delta = args.Key is VirtualKey.Left or VirtualKey.Down ? -0.01 : args.Key is VirtualKey.Right or VirtualKey.Up ? 0.01 : 0;
            if (delta != 0)
            {
                SetStopOffset(index, stops[index].Offset + delta);
                args.Handled = true;
            }
        }
    }

    private void OnStopKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (sender is Button { Tag: int index })
        {
            SelectStop(index);
            HandleStopKey(index, args);
        }
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs args)
    {
        var revision = _revision;
        var request = ++_browseRequest;
        BrowseButton.IsEnabled = false;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            foreach (var extension in BrushImageImport.Extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }
            var file = await picker.PickSingleFileAsync();
            if (file == null || !IsCurrentImport(request, revision))
            {
                return;
            }
            await BrushImageImport.RunAsync(
                () => file.OpenStreamForReadAsync(),
                file.FileType,
                Path.Combine(ApplicationData.Current.LocalFolder.Path, "BrushAssets"),
                async path =>
                {
                    var imported = await StorageFile.GetFileFromPathAsync(path);
                    using var stream = await imported.OpenReadAsync();
                    await new BitmapImage().SetSourceAsync(stream);
                },
                () => IsCurrentImport(request, revision),
                uri => Publish(BrushEditorOperations.Edit(Brush!, copy => ((ImageBrush)copy).ImageSource = new BitmapImage(uri))));
        }
        catch (Exception exception)
        {
            if (IsCurrentImport(request, revision))
            {
                ShowError(exception);
            }
        }
        finally
        {
            if (request == _browseRequest)
            {
                BrowseButton.IsEnabled = true;
            }
        }
    }

    private bool IsCurrentImport(long request, long revision) => _loaded && request == _browseRequest && revision == _revision && Brush is ImageBrush;

    private void ShowError(Exception exception)
    {
        ErrorText.Text = $"无法更新笔刷：{exception.Message}";
        ErrorText.Margin = new Thickness(0, 8, 0, 0);
        ErrorText.Visibility = Visibility.Visible;
    }

    private void UpdateAdorner()
    {
        if (!_ready || !_loaded || TargetElement == null || BrushEditorOperations.Stops(Brush) == null)
        {
            _adorner?.Hide();
            return;
        }
        _adorner ??= new BrushGradientAdorner(this);
        _adorner.Update();
    }
}
