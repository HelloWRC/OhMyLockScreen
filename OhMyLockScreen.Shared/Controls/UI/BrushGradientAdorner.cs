using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using OhMyLockScreen.Shared.Helpers.UI;
using RadialGradientBrush = Microsoft.UI.Xaml.Media.RadialGradientBrush;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>A view-local, non-modal gradient editing surface. Only handles receive pointer input.</summary>
internal sealed class BrushGradientAdorner : IDisposable
{
    private readonly BrushEditControl _editor;
    private readonly Canvas _surface = new();
    private readonly Popup _popup;
    private readonly Dictionary<string, Button> _handles = [];
    private readonly Line _axis = NewLine();
    private readonly Line _radiusAxis = NewLine();
    private FrameworkElement? _root;
    private BrushEditorMatrix _toRoot;
    private BrushEditorMatrix _fromRoot;
    private string? _dragKey;
    private Button? _capturedHandle;
    private uint _pointerId;
    private bool _updating;
    private bool _disposed;
    private bool _ownedFocus;
    private Point _axisStart;
    private Point _axisEnd;
    private Point _grabOffset;

    internal BrushGradientAdorner(BrushEditControl editor)
    {
        _editor = editor;
        _popup = new Popup { Child = _surface, IsLightDismissEnabled = false };
        _surface.Children.Add(_axis);
        _surface.Children.Add(_radiusAxis);
        _surface.GotFocus += OnFocusChanged;
        _surface.LostFocus += OnFocusChanged;
    }

    private static Line NewLine() => new()
    {
        Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
        StrokeThickness = 1,
        IsHitTestVisible = false
    };

    internal void Update()
    {
        // A focus update queued before the target changed can run after disposal.
        if (_disposed || _updating)
        {
            return;
        }
        _updating = true;
        try
        {
            var target = _editor.TargetElement;
            var brush = _editor.Brush;
            var root = FindRoot(_editor);
            if (target == null || brush == null || root == null || !ReferenceEquals(FindRoot(target), root)
                || target.ActualWidth <= 0 || target.ActualHeight <= 0 || target.Visibility != Visibility.Visible)
            {
                Hide();
                return;
            }
            if (_root != root)
            {
                if (_root != null)
                {
                    _root.LayoutUpdated -= OnLayoutUpdated;
                }
                _root = root;
                _root.LayoutUpdated += OnLayoutUpdated;
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
                {
                    _popup.XamlRoot = root.XamlRoot;
                }
            }
            var focused = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) && root.XamlRoot != null
                ? FocusManager.GetFocusedElement(root.XamlRoot) : FocusManager.GetFocusedElement();
            var inEditor = IsWithin(focused as DependencyObject, _editor);
            var inSurface = IsWithin(focused as DependencyObject, _surface);
            // ComboBox and ColorPicker flyouts can have a separate visual tree. A popup
            // owned by this editor keeps its logical focus while that flyout is active.
            var inEditorPopup = VisualTreeHelper.GetOpenPopups(Window.Current)
                .Any(popup => popup != _popup && popup.Child is DependencyObject child
                    && IsWithin(focused as DependencyObject, child)
                    && _ownedFocus);
            _ownedFocus = inEditor || inSurface || inEditorPopup;
            if (!inEditor && !inSurface && !inEditorPopup && _dragKey == null)
            {
                Hide();
                return;
            }

            var visual = target.TransformToVisual(root);
            var origin = visual.TransformPoint(new Point(0, 0));
            var unitX = visual.TransformPoint(new Point(1, 0));
            var unitY = visual.TransformPoint(new Point(0, 1));
            var targetMap = new BrushEditorMatrix(unitX.X - origin.X, unitX.Y - origin.Y,
                unitY.X - origin.X, unitY.Y - origin.Y, origin.X, origin.Y);
            _toRoot = BrushEditorMatrix.ForBrush(brush, target.ActualWidth, target.ActualHeight).Then(targetMap);
            if (!_toRoot.TryInverse(out _fromRoot))
            {
                Hide();
                return;
            }

            _surface.Width = root.ActualWidth;
            _surface.Height = root.ActualHeight;
            _surface.RequestedTheme = _editor.ActualTheme;
            _surface.Clip = new RectangleGeometry { Rect = VisibleBounds(target, root) };
            var active = new HashSet<string>();
            Point start;
            Point end;
            switch (brush)
            {
                case LinearGradientBrush linear:
                    start = linear.StartPoint;
                    end = linear.EndPoint;
                    Place("start", "渐变起点", start, BrushEditorOperations.White, false, active);
                    Place("end", "渐变终点", end, BrushEditorOperations.White, false, active);
                    _radiusAxis.Visibility = Visibility.Collapsed;
                    break;
                case RadialGradientBrush radial:
                    start = radial.GradientOrigin;
                    end = new Point(radial.Center.X + radial.RadiusX, radial.Center.Y);
                    Place("center", "径向渐变中心", radial.Center, BrushEditorOperations.White, false, active);
                    Place("origin", "径向渐变原点", radial.GradientOrigin, Color.FromArgb(255, 96, 205, 255), false, active);
                    Place("radiusX", "水平半径", end, BrushEditorOperations.White, false, active);
                    var bottom = new Point(radial.Center.X, radial.Center.Y + radial.RadiusY);
                    Place("radiusY", "垂直半径", bottom, BrushEditorOperations.White, false, active);
                    SetLine(_radiusAxis, radial.Center, bottom);
                    _radiusAxis.Visibility = Visibility.Visible;
                    break;
                default:
                    Hide();
                    return;
            }
            SetLine(_axis, start, end);
            _axisStart = _toRoot.Map(start);
            _axisEnd = _toRoot.Map(end);
            var stops = BrushEditorOperations.Stops(brush)!;
            for (var index = 0; index < stops.Count; index++)
            {
                var stop = stops[index];
                Place($"stop:{index}", $"色标 {index + 1}，位置 {stop.Offset:P1}",
                    new Point(start.X + (end.X - start.X) * stop.Offset, start.Y + (end.Y - start.Y) * stop.Offset),
                    stop.Color, index == _editor.SelectedStop, active);
            }
            foreach (var key in _handles.Keys.Where(key => !active.Contains(key)).ToArray())
            {
                _surface.Children.Remove(_handles[key]);
                _handles.Remove(key);
            }
            _popup.IsOpen = true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // A target can leave the tree or change its transform during a layout pass.
            System.Diagnostics.Debug.WriteLine(exception);
            Hide();
        }
        finally
        {
            _updating = false;
        }
    }

    private static FrameworkElement? FindRoot(FrameworkElement element)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) && element.XamlRoot?.Content is FrameworkElement root)
        {
            return root;
        }
        DependencyObject current = element;
        while (VisualTreeHelper.GetParent(current) is { } parent)
        {
            current = parent;
        }
        return ReferenceEquals(current, Window.Current.Content) ? current as FrameworkElement : null;
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }
        return false;
    }

    private static Rect VisibleBounds(FrameworkElement target, FrameworkElement root)
    {
        var bounds = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
        for (DependencyObject? current = target; current != null && current != root; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement element)
            {
                if (element.Visibility != Visibility.Visible)
                {
                    return new Rect();
                }
                var clip = element.Clip?.Bounds;
                if (element is ScrollContentPresenter)
                {
                    clip = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
                }
                if (clip.HasValue)
                {
                    bounds.Intersect(element.TransformToVisual(root).TransformBounds(clip.Value));
                }
            }
        }
        return bounds.IsEmpty ? new Rect() : bounds;
    }

    private void Place(string key, string label, Point position, Color color, bool selected, HashSet<string> active)
    {
        active.Add(key);
        if (!_handles.TryGetValue(key, out var button))
        {
            button = _editor.CreateHandle(OnPressed, OnMoved, OnReleased, OnCanceled, OnKeyDown);
            button.Tag = key;
            button.Click += OnHandleClick;
            button.GotFocus += OnHandleGotFocus;
            if (!key.StartsWith("stop:", StringComparison.Ordinal))
            {
                button.CornerRadius = new CornerRadius(0);
                button.RenderTransformOrigin = new Point(0.5, 0.5);
                button.RenderTransform = new RotateTransform { Angle = 45 };
            }
            _handles.Add(key, button);
            _surface.Children.Add(button);
        }
        BrushEditControl.PaintHandle(button, color, selected);
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        var point = _toRoot.Map(position);
        if (key == "center")
        {
            point = new Point(point.X - 20, point.Y - 20);
        }
        if (StopIndex(key) != null)
        {
            // Separate color stops from the coincident geometry endpoints so both
            // remain reachable with a pointer, even at offsets zero and one.
            var dx = _axisEnd.X - _axisStart.X;
            var dy = _axisEnd.Y - _axisStart.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            point = new Point(point.X - (length > 0 ? dy / length : 0) * 18,
                point.Y + (length > 0 ? dx / length : 1) * 18);
        }
        Canvas.SetLeft(button, point.X - 14);
        Canvas.SetTop(button, point.Y - 14);
    }

    private void SetLine(Line line, Point start, Point end)
    {
        var first = _toRoot.Map(start);
        var last = _toRoot.Map(end);
        line.X1 = first.X;
        line.Y1 = first.Y;
        line.X2 = last.X;
        line.Y2 = last.Y;
    }

    private void OnPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_dragKey != null || sender is not Button { Tag: string key } button || !BrushEditControl.IsPrimaryHandlePress(button, args))
        {
            return;
        }
        if (StopIndex(key) is { } index)
        {
            _editor.SelectStop(index);
        }
        button.Focus(FocusState.Pointer);
        if (BrushEditControl.CaptureHandlePointer(button, args))
        {
            _dragKey = key;
            _capturedHandle = button;
            _pointerId = args.Pointer.PointerId;
            var pointer = _fromRoot.Map(args.GetCurrentPoint(_surface).Position);
            var position = PositionFor(key);
            _grabOffset = new Point(position.X - pointer.X, position.Y - pointer.Y);
        }
        args.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_dragKey != null && args.Pointer.PointerId == _pointerId)
        {
            var point = _fromRoot.Map(args.GetCurrentPoint(_surface).Position);
            Move(_dragKey, new Point(point.X + _grabOffset.X, point.Y + _grabOffset.Y));
            args.Handled = true;
        }
    }

    private void OnReleased(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(sender, _capturedHandle) && args.Pointer.PointerId == _pointerId)
        {
            CancelDrag();
            args.Handled = true;
        }
    }

    private void OnCanceled(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(sender, _capturedHandle) && args.Pointer.PointerId == _pointerId)
        {
            CancelDrag();
        }
    }

    private void OnHandleClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string key } && StopIndex(key) is { } index)
        {
            _editor.SelectStop(index);
        }
    }

    private void OnHandleGotFocus(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string key } && StopIndex(key) is { } index && _editor.SelectedStop != index)
        {
            _editor.SelectStop(index);
        }
    }

    internal void CancelDrag()
    {
        var captured = _capturedHandle;
        _capturedHandle = null;
        _dragKey = null;
        captured?.ReleasePointerCaptures();
    }

    private static int? StopIndex(string key) => key.StartsWith("stop:", StringComparison.Ordinal)
        && int.TryParse(key.AsSpan(5), out var index) ? index : null;

    private void Move(string key, Point point) => MoveCore(key, point, roundStopOffset: true);

    private void MoveCore(string key, Point point, bool roundStopOffset)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            return;
        }
        if (StopIndex(key) is { } index)
        {
            var offset = BrushEditorMatrix.Project(_toRoot.Map(point), _axisStart, _axisEnd);
            _editor.SetStopOffset(index, roundStopOffset ? BrushEditorOperations.RoundDraggedOffset(offset) : offset);
            return;
        }
        _editor.Edit(copy =>
        {
            if (copy is LinearGradientBrush linear)
            {
                if (key == "start")
                {
                    linear.StartPoint = point;
                }
                else
                {
                    linear.EndPoint = point;
                }
            }
            else if (copy is RadialGradientBrush radial)
            {
                switch (key)
                {
                    case "center":
                        radial.Center = point;
                        break;
                    case "origin":
                        radial.GradientOrigin = point;
                        break;
                    case "radiusX":
                        radial.RadiusX = Math.Abs(point.X - radial.Center.X);
                        break;
                    case "radiusY":
                        radial.RadiusY = Math.Abs(point.Y - radial.Center.Y);
                        break;
                }
            }
        });
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }
        if (StopIndex(key) is { } index)
        {
            _editor.SelectStop(index);
            _editor.HandleStopKey(index, args);
            return;
        }
        var step = _editor.Brush is LinearGradientBrush { MappingMode: BrushMappingMode.Absolute }
            or RadialGradientBrush { MappingMode: BrushMappingMode.Absolute } ? 1 : 0.01;
        var x = args.Key == VirtualKey.Left ? -step : args.Key == VirtualKey.Right ? step : 0;
        var y = args.Key == VirtualKey.Up ? -step : args.Key == VirtualKey.Down ? step : 0;
        if (x == 0 && y == 0)
        {
            return;
        }
        var position = PositionFor(key);
        MoveCore(key, new Point(position.X + x, position.Y + y), roundStopOffset: false);
        args.Handled = true;
    }

    private Point PositionFor(string key)
    {
        if (StopIndex(key) is { } index && BrushEditorOperations.Stops(_editor.Brush) is { } stops && index < stops.Count)
        {
            var point = new Point(_axisStart.X + (_axisEnd.X - _axisStart.X) * stops[index].Offset,
                _axisStart.Y + (_axisEnd.Y - _axisStart.Y) * stops[index].Offset);
            return _fromRoot.Map(point);
        }
        return _editor.Brush switch
        {
            LinearGradientBrush linear => key == "start" ? linear.StartPoint : linear.EndPoint,
            RadialGradientBrush radial => key switch
            {
                "center" => radial.Center,
                "origin" => radial.GradientOrigin,
                "radiusX" => new Point(radial.Center.X + radial.RadiusX, radial.Center.Y),
                _ => new Point(radial.Center.X, radial.Center.Y + radial.RadiusY)
            },
            _ => new Point()
        };
    }

    private void OnFocusChanged(object sender, RoutedEventArgs args) =>
        _ = _editor.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, Update);

    private void OnLayoutUpdated(object? sender, object args) => Update();

    internal void Hide()
    {
        CancelDrag();
        _popup.IsOpen = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _surface.GotFocus -= OnFocusChanged;
        _surface.LostFocus -= OnFocusChanged;
        Hide();
        if (_root != null)
        {
            _root.LayoutUpdated -= OnLayoutUpdated;
            _root = null;
        }
        _popup.Child = null;
        _surface.Children.Clear();
        _handles.Clear();
    }
}
