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
        internal PropertyMetadata Metadata { get; }

        private DependencyProperty(PropertyMetadata metadata) => Metadata = metadata;

        public static DependencyProperty Register(string name, Type type, Type owner, PropertyMetadata metadata)
            => new(metadata);
    }

    public class DependencyObject
    {
        private readonly Dictionary<DependencyProperty, object?> _values = [];

        public object? GetValue(DependencyProperty property)
            => _values.TryGetValue(property, out var value) ? value : property.Metadata.DefaultValue;

        public void SetValue(DependencyProperty property, object? value)
        {
            var previous = GetValue(property);
            if (Equals(previous, value))
            {
                return;
            }

            _values[property] = value;
            property.Metadata.Callback?.Invoke(this, new DependencyPropertyChangedEventArgs(previous, value));
        }
    }

    public sealed class PropertyPath(string path)
    {
        public string Path { get; } = path;
    }

    public class UIElement : DependencyObject;

    public class FrameworkElement : UIElement
    {
        public object? DataContext { get; set; }
        public Dictionary<object, object> Resources { get; } = [];
        public Core.CoreDispatcher Dispatcher { get; } = new(Window.Current);

        public event UwpEventHandler<RoutedEventArgs>? Loaded;
        public event UwpEventHandler<RoutedEventArgs>? Unloaded;
        public event UwpEventHandler<object>? LayoutUpdated;

        public Dictionary<DependencyProperty, Data.Binding> Bindings { get; } = [];

        public void SetBinding(DependencyProperty property, Data.Binding binding) => Bindings[property] = binding;

        public void Load() => Loaded?.Invoke(this, new RoutedEventArgs());

        public void Unload() => Unloaded?.Invoke(this, new RoutedEventArgs());

        public void RaiseLayoutUpdated() => LayoutUpdated?.Invoke(this, EventArgs.Empty);
    }
}

namespace Windows.UI.Xaml.Data
{
    public enum BindingMode { OneWay, TwoWay, OneTime }

    public sealed class Binding
    {
        public object? Source { get; set; }
        public PropertyPath? Path { get; set; }
        public BindingMode Mode { get; set; }
    }
}

namespace Windows.UI.Xaml.Controls
{
    public class FontIconSource : DependencyObject
    {
        public string Glyph { get; set; } = string.Empty;
        public Media.FontFamily? FontFamily { get; set; }
    }

    public class Control : FrameworkElement
    {
        public object? DefaultStyleKey { get; set; }
        protected virtual void OnApplyTemplate() { }

    }

    public class UserControl : Control
    {
        public object? Content { get; set; }
    }

    public class ContentPresenter : FrameworkElement
    {
        public object? Content { get; set; }
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
        public FluentIconSource()
        {
            FontFamily = new Windows.UI.Xaml.Media.FontFamily(
                "ms-appx:///FluentIcons.Uwp/Assets/FluentSystemIcons-Size20.otf#Fluent System Icons Size20");
        }
    }
}
