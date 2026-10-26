// Storage, model editing and routing are tested here; native XAML is verified by the UWP build.
namespace Windows.UI.Text
{
    public static class FontWeights
    {
        public static object Bold { get; } = new();
    }
}

namespace OhMyLockScreen
{
    public sealed class SceneEditorPage : Windows.UI.Xaml.Controls.Page;
}

namespace OhMyLockScreen.Controls.Editor
{
    public sealed class BackgroundSettingsControl : BackgroundSettingsControlBase;
    public sealed class ForegroundSettingsControl : ForegroundSettingsControlBase;
    public sealed class LayoutSettingsControl : LayoutSettingsControlBase;
    public sealed class GridLayoutSettingsControl : GridLayoutSettingsControlBase;
}

namespace OhMyLockScreen.Controls.Components
{
    public sealed class TextSettingsControl : TextSettingsControlBase;
    public sealed class ClockSettingsControl : ClockSettingsControlBase;
    public sealed class ContainerSettingsControl : ContainerSettingsControlBase;
    internal sealed class TestCompiledBindings(Action update)
    {
        internal void Update() => update();
    }

    public sealed partial class TextComponent
    {
        private new TestCompiledBindings Bindings { get; set; } = null!;

        private void InitializeComponent()
        {
            // Stand-in for generated XAML bindings. The UWP build verifies the actual markup.
            var text = new Windows.UI.Xaml.Controls.TextBlock();
            Content = text;
            Bindings = new TestCompiledBindings(() =>
            {
                text.SetBinding(Windows.UI.Xaml.Controls.TextBlock.TextProperty, new Windows.UI.Xaml.Data.Binding { Source = Settings, Path = new Windows.UI.Xaml.PropertyPath(nameof(Settings.Text)), Mode = Windows.UI.Xaml.Data.BindingMode.OneWay });
                text.SetBinding(Windows.UI.Xaml.Controls.TextBlock.FontSizeProperty, new Windows.UI.Xaml.Data.Binding { Source = Settings, Path = new Windows.UI.Xaml.PropertyPath(nameof(Settings.FontSize)), Mode = Windows.UI.Xaml.Data.BindingMode.OneWay });
            });
        }
    }

    public sealed partial class ClockComponent
    {
        private new TestCompiledBindings Bindings
        {
            get;
        } = new(() =>
        {
        });

        private void InitializeComponent()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }
    }

    public sealed partial class DateComponent
    {
        private new TestCompiledBindings Bindings { get; set; } = null!;

        private void InitializeComponent()
        {
            var text = new Windows.UI.Xaml.Controls.TextBlock();
            Content = text;
            Bindings = new TestCompiledBindings(() => text.SetBinding(
                Windows.UI.Xaml.Controls.TextBlock.TextProperty,
                new Windows.UI.Xaml.Data.Binding
                {
                    Source = this,
                    Path = new Windows.UI.Xaml.PropertyPath(nameof(DisplayText)),
                    Mode = Windows.UI.Xaml.Data.BindingMode.OneWay
                }));
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }
    }

    public sealed partial class ContainerComponent
    {
        private new TestCompiledBindings Bindings { get; set; } = null!;

        private void InitializeComponent()
        {
            var layout = new OhMyLockScreen.Shared.Controls.Scene.LayoutControl();
            Content = layout;
            Bindings = new TestCompiledBindings(() =>
            {
                layout.Settings = Settings;
                layout.ItemsSource = Settings.Children;
            });
        }
    }
}
