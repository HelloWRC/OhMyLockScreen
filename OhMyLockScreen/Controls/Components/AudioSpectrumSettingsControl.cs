using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Controls.Components;

public sealed class AudioSpectrumSettingsControl : ControlWithSettingsBase<AudioSpectrumComponentSettings>
{
    protected override void OnSettingsChanged()
    {
        var panel = new StackPanel { Style = (Style)Application.Current.Resources["EditorFormStyle"] };
        AddChoice(panel, "贴靠方向", nameof(Settings.Dock), ["上", "下", "左", "右"]);
        AddChoice(panel, "颜色来源", nameof(Settings.ColorSource), ["媒体封面主题色", "前景画刷"]);
        AddNumber(panel, "柱数", nameof(Settings.BarCount), 8, 256, 1);
        AddNumber(panel, "间距", nameof(Settings.Gap), 0, 20, 0.5);
        AddNumber(panel, "灵敏度", nameof(Settings.Sensitivity), 0.1, 5, 0.1);
        panel.Children.Add(new TextBlock
        {
            Text = "没有媒体封面时使用前景画刷，可通过前景特性设置画刷。",
            TextWrapping = TextWrapping.Wrap
        });
        Content = panel;
    }

    private Binding Bind(string property) => new()
    {
        Source = Settings,
        Path = new PropertyPath(property),
        Mode = BindingMode.TwoWay
    };

    private void AddChoice(StackPanel panel, string header, string property, string[] choices)
    {
        var combo = new ComboBox { Header = header, ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Stretch };
        var binding = Bind(property);
        binding.Converter = new EnumIndexConverter();
        combo.SetBinding(Selector.SelectedIndexProperty, binding);
        panel.Children.Add(combo);
    }

    private void AddNumber(StackPanel panel, string header, string property, double minimum, double maximum, double step)
    {
        var number = new Microsoft.UI.Xaml.Controls.NumberBox
        {
            Header = header,
            Minimum = minimum,
            Maximum = maximum,
            SmallChange = step,
            Style = (Style)Application.Current.Resources["EditorNumberStyle"]
        };
        var binding = Bind(property);
        binding.Converter = new FiniteNumberConverter();
        number.SetBinding(Microsoft.UI.Xaml.Controls.NumberBox.ValueProperty, binding);
        panel.Children.Add(number);
    }
}
