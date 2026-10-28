using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Controls.Components;

public sealed class SystemStatusSettingsControl : ControlWithSettingsBase<SystemStatusComponentSettings>
{
    protected override void OnSettingsChanged()
    {
        var panel = new StackPanel { Style = (Style)Application.Current.Resources["EditorFormStyle"] };
        var kind = new ComboBox
        {
            Header = "状态类型",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "电量", "网络", "音量", "蓝牙" }
        };
        var binding = Bind(nameof(Settings.Kind));
        binding.Converter = new EnumIndexConverter();
        kind.SetBinding(Selector.SelectedIndexProperty, binding);
        panel.Children.Add(kind);
        AddToggle(panel, "显示图标", nameof(Settings.ShowIcon));
        AddToggle(panel, "显示文字", nameof(Settings.ShowText));
        var iconSize = new Microsoft.UI.Xaml.Controls.NumberBox
        {
            Header = "图标大小",
            Minimum = 1,
            Maximum = 512,
            Style = (Style)Application.Current.Resources["EditorNumberStyle"]
        };
        var sizeBinding = Bind(nameof(Settings.IconSize));
        sizeBinding.Converter = new FiniteNumberConverter();
        iconSize.SetBinding(Microsoft.UI.Xaml.Controls.NumberBox.ValueProperty, sizeBinding);
        panel.Children.Add(iconSize);
        Content = panel;
    }

    private void AddToggle(StackPanel panel, string label, string property)
    {
        var toggle = new CheckBox { Content = label };
        toggle.SetBinding(ToggleButton.IsCheckedProperty, Bind(property));
        panel.Children.Add(toggle);
    }

    private Binding Bind(string property) => new()
    {
        Source = Settings,
        Path = new PropertyPath(property),
        Mode = BindingMode.TwoWay
    };
}
