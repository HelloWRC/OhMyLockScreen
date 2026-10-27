// Minimal dependency-property and binding declarations for framework tests.
// These capture binding contracts; they do not emulate the UWP binding engine.
namespace Windows.UI.Xaml
{
    public delegate void PropertyChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs args);
    public sealed class DependencyPropertyChangedEventArgs(object? oldValue, object? newValue)
    {
        public object? OldValue { get; } = oldValue;
        public object? NewValue { get; } = newValue;
    }

    public sealed class PropertyMetadata(object? defaultValue, PropertyChangedCallback? callback = null)
    {
        internal object? DefaultValue { get; } = defaultValue;
        internal PropertyChangedCallback? Callback { get; } = callback;
    }

    public sealed class DependencyProperty
    {
        public static object UnsetValue { get; } = new();
        internal PropertyMetadata Metadata
        {
            get;
        }

        private readonly Type? _owner;

        private DependencyProperty(PropertyMetadata metadata, Type? owner)
        {
            Metadata = metadata;
            _owner = owner;
        }

        public static DependencyProperty Register(string name, Type type, Type owner, PropertyMetadata metadata) => new(metadata, owner);
        public static DependencyProperty RegisterAttached(string name, Type type, Type owner, PropertyMetadata metadata) => new(metadata, null);

        internal void ValidateOwner(DependencyObject target)
        {
            if (_owner != null && !_owner.IsInstanceOfType(target))
            {
                throw new global::System.Runtime.InteropServices.COMException("The dependency property does not belong to this target.");
            }
        }
    }

    public class DependencyObject
    {
        public DependencyObject? VisualParent { get; set; }
        private readonly Dictionary<DependencyProperty, object?> _values = [];
        public object? GetValue(DependencyProperty property)
        {
            property.ValidateOwner(this);
            return _values.TryGetValue(property, out var value) ? value : property.Metadata.DefaultValue;
        }
        public void SetValue(DependencyProperty property, object? value)
        {
            var previous = GetValue(property);
            if (this is FrameworkElement element)
            {
                element.Bindings.Remove(property);
            }

            _values[property] = value;
            if (Equals(previous, value))
            {
                return;
            }

            property.Metadata.Callback?.Invoke(this, new DependencyPropertyChangedEventArgs(previous, value));
        }

        public object? ReadLocalValue(DependencyProperty property)
        {
            property.ValidateOwner(this);
            return _values.TryGetValue(property, out var value) ? value : DependencyProperty.UnsetValue;
        }

        public void ClearValue(DependencyProperty property)
        {
            var previous = GetValue(property);
            _values.Remove(property);
            if (this is FrameworkElement element)
            {
                element.Bindings.Remove(property);
            }

            var current = GetValue(property);
            if (!Equals(previous, current))
            {
                property.Metadata.Callback?.Invoke(this, new DependencyPropertyChangedEventArgs(previous, current));
            }
        }
    }

    public sealed class PropertyPath(string path)
    {
        public string Path { get; } = path;
    }

    public class UIElement : DependencyObject
    {
        public Media.RectangleGeometry? Clip { get; set; }

        public static readonly DependencyProperty OpacityProperty = DependencyProperty.Register(
            nameof(Opacity),
            typeof(double),
            typeof(UIElement),
            new PropertyMetadata(1.0));
        public static readonly DependencyProperty VisibilityProperty = DependencyProperty.Register(
            nameof(Visibility),
            typeof(Visibility),
            typeof(UIElement),
            new PropertyMetadata(Visibility.Visible));

        public double Opacity
        {
            get => (double)GetValue(OpacityProperty)!;
            set => SetValue(OpacityProperty, value);
        }

        public Visibility Visibility
        {
            get => (Visibility)GetValue(VisibilityProperty)!;
            set => SetValue(VisibilityProperty, value);
        }
    }
    public enum HorizontalAlignment
    {
        Left,
        Center,
        Right,
        Stretch
    }

    public enum VerticalAlignment
    {
        Top,
        Center,
        Bottom,
        Stretch
    }

    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double uniform) : this(uniform, uniform, uniform, uniform) { }
    }

    public readonly record struct CornerRadius(double TopLeft, double TopRight, double BottomRight, double BottomLeft)
    {
        public CornerRadius(double uniform) : this(uniform, uniform, uniform, uniform) { }
    }
    public class FrameworkElement : UIElement
    {
        public Media.GeneralTransform TransformToVisual(UIElement visual) => new();
        public XamlRoot? XamlRoot { get; set; }
        public ElementTheme ActualTheme { get; set; }
        public ElementTheme RequestedTheme { get; set; }
        public double MaxWidth { get; set; }
        public event Action<FrameworkElement, object>? ActualThemeChanged;
        public void ChangeTheme(ElementTheme theme)
        {
            ActualTheme = theme;
            ActualThemeChanged?.Invoke(this, EventArgs.Empty);
        }
        public double ActualWidth
        {
            get;
            set;
        }

        public double ActualHeight
        {
            get;
            set;
        }

        public Media.Transform? RenderTransform
        {
            get; set;
        }

        public static readonly DependencyProperty WidthProperty = DependencyProperty.Register(nameof(Width), typeof(double), typeof(FrameworkElement), new PropertyMetadata(double.NaN));
        public static readonly DependencyProperty HeightProperty = DependencyProperty.Register(nameof(Height), typeof(double), typeof(FrameworkElement), new PropertyMetadata(double.NaN));
        public static readonly DependencyProperty HorizontalAlignmentProperty = DependencyProperty.Register(nameof(HorizontalAlignment), typeof(HorizontalAlignment), typeof(FrameworkElement), new PropertyMetadata(HorizontalAlignment.Stretch));
        public static readonly DependencyProperty VerticalAlignmentProperty = DependencyProperty.Register(nameof(VerticalAlignment), typeof(VerticalAlignment), typeof(FrameworkElement), new PropertyMetadata(VerticalAlignment.Stretch));
        public static readonly DependencyProperty MarginProperty = DependencyProperty.Register(nameof(Margin), typeof(Thickness), typeof(FrameworkElement), new PropertyMetadata(default(Thickness)));
        public double Width
        {
            get => (double)GetValue(WidthProperty)!; set => SetValue(WidthProperty, value);
        }
        public double Height
        {
            get => (double)GetValue(HeightProperty)!; set => SetValue(HeightProperty, value);
        }
        public HorizontalAlignment HorizontalAlignment
        {
            get => (HorizontalAlignment)GetValue(HorizontalAlignmentProperty)!; set => SetValue(HorizontalAlignmentProperty, value);
        }
        public VerticalAlignment VerticalAlignment
        {
            get => (VerticalAlignment)GetValue(VerticalAlignmentProperty)!; set => SetValue(VerticalAlignmentProperty, value);
        }
        public Thickness Margin
        {
            get => (Thickness)GetValue(MarginProperty)!; set => SetValue(MarginProperty, value);
        }
        public object? DataContext
        {
            get; set;
        }
        public Dictionary<object, object> Resources { get; } = [];
        public Core.CoreDispatcher Dispatcher { get; } = new(Window.Current);

        public event UwpEventHandler<RoutedEventArgs>? Loaded;
        public event UwpEventHandler<RoutedEventArgs>? Unloaded;
        public event UwpEventHandler<SizeChangedEventArgs>? SizeChanged;
        public void Resize(double width, double height)
        {
            ActualWidth = width;
            ActualHeight = height;
            SizeChanged?.Invoke(this, new SizeChangedEventArgs());
        }
        public event UwpEventHandler<object>? LayoutUpdated;
        public Dictionary<DependencyProperty, Data.Binding> Bindings { get; } = [];

        public void SetBinding(DependencyProperty property, Data.Binding binding) => Bindings[property] = binding;
        public Data.BindingExpression? GetBindingExpression(DependencyProperty property) =>
            Bindings.TryGetValue(property, out var binding) ? new Data.BindingExpression(binding) : null;
        public void Load() => Loaded?.Invoke(this, new RoutedEventArgs());
        public void Unload() => Unloaded?.Invoke(this, new RoutedEventArgs());
        public void RaiseLayoutUpdated() => LayoutUpdated?.Invoke(this, EventArgs.Empty);
    }
}

namespace Windows.UI.Xaml.Data
{
    public sealed class BindingExpression(Binding binding)
    {
        public Binding ParentBinding { get; } = binding;
    }

    public static class BindingOperations
    {
        public static void SetBinding(DependencyObject target, DependencyProperty property, Binding binding) =>
            ((FrameworkElement)target).SetBinding(property, binding);
    }

    public interface IValueConverter
    {
        object Convert(object value, Type targetType, object parameter, string language);
        object ConvertBack(object value, Type targetType, object parameter, string language);
    }

    public enum BindingMode
    {
        OneWay,
        TwoWay,
        OneTime
    }

    public sealed class Binding
    {
        public object? Source
        {
            get; set;
        }
        public PropertyPath? Path
        {
            get; set;
        }
        public BindingMode Mode
        {
            get; set;
        }
    }
}

namespace Windows.UI.Xaml.Controls
{
    public class FontIconSource : DependencyObject
    {
        public double FontSize
        {
            get; set;
        }
        public string Glyph { get; set; } = string.Empty;
        public Media.FontFamily? FontFamily
        {
            get; set;
        }
    }

    public partial class Control : FrameworkElement
    {
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Windows.UI.Xaml.Media.Brush), typeof(Control), new PropertyMetadata(null));

        public Windows.UI.Xaml.Media.Brush? Foreground
        {
            get => (Windows.UI.Xaml.Media.Brush?)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
            nameof(Background), typeof(Windows.UI.Xaml.Media.Brush), typeof(Control), new PropertyMetadata(null));

        public Windows.UI.Xaml.Media.Brush? Background
        {
            get => (Windows.UI.Xaml.Media.Brush?)GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        public object? DefaultStyleKey
        {
            get; set;
        }

        protected virtual void OnApplyTemplate()
        {
        }
    }

    public class UserControl : Control
    {
        public object? Content
        {
            get; set;
        }
    }

    public partial class ContentPresenter : FrameworkElement
    {
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Windows.UI.Xaml.Media.Brush), typeof(ContentPresenter), new PropertyMetadata(null));

        public Windows.UI.Xaml.Media.Brush? Foreground
        {
            get => (Windows.UI.Xaml.Media.Brush?)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public object? Content
        {
            get; set;
        }
    }

    public enum Stretch
    {
        Fill,
        Uniform,
        UniformToFill
    }
}

namespace Windows.UI.Xaml.Documents
{
    public class TextElement : DependencyObject
    {
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Windows.UI.Xaml.Media.Brush), typeof(TextElement), new PropertyMetadata(null));

        public Windows.UI.Xaml.Media.Brush? Foreground
        {
            get => (Windows.UI.Xaml.Media.Brush?)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }
    }
}

namespace Windows.UI.Xaml.Media
{
    public sealed class FontFamily(string source)
    {
        public string Source { get; } = source;
    }
}

// The portable harness checks icon metadata and source ownership, not font rendering.
namespace FluentIcons.Uwp
{
    public sealed class FluentIconSource : Windows.UI.Xaml.Controls.FontIconSource
    {
        public FluentIcons.Common.Icon Icon
        {
            get; set;
        }
        public FluentIcons.Common.IconSize IconSize
        {
            get; set;
        }

        public FluentIconSource()
        {
            FontFamily = new Windows.UI.Xaml.Media.FontFamily("ms-appx:///FluentIcons.Uwp/Assets/FluentSystemIcons-Size20.otf#Fluent System Icons Size20");
        }
    }
}
