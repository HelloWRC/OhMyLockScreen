using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Controls.Components;

public sealed class WeatherComponentSettingsControl : ControlWithSettingsBase<WeatherComponentSettings>
{
    protected override void OnSettingsChanged()
    {
        var panel = new StackPanel { Style = (Style)Application.Current.Resources["EditorFormStyle"] };

        panel.Children.Add(CreateToggle("显示天气信息", nameof(Settings.ShowSummary)));
        var summaryKind = CreateChoice("天气信息类型", nameof(Settings.SummaryKind),
            ["天气类型/气温", "湿度", "风力", "空气质量", "气压", "体感温度"]);
        summaryKind.SetBinding(VisibilityProperty, VisibleWhen(nameof(Settings.ShowSummary)));
        panel.Children.Add(summaryKind);

        panel.Children.Add(CreateToggle("显示预警信号", nameof(Settings.ShowAlerts)));
        var alertTextMode = CreateChoice("显示预警类型", nameof(Settings.AlertTextMode),
            ["不显示", "仅当没有对应图标时", "总是显示"]);
        alertTextMode.SetBinding(VisibilityProperty, VisibleWhen(nameof(Settings.ShowAlerts)));
        panel.Children.Add(alertTextMode);

        panel.Children.Add(CreateToggle("显示降水提示", nameof(Settings.ShowRainTime)));
        var approximateRain = CreateToggle("模糊长时间降雨倒计时", nameof(Settings.ApproximateLongRainTime));
        approximateRain.Margin = new Thickness(24, 0, 0, 0);
        ToolTipService.SetToolTip(approximateRain, "开启后，大于 60 分钟的降雨时间将以 0.5 小时为单位模糊显示。");
        approximateRain.SetBinding(VisibilityProperty, VisibleWhen(nameof(Settings.ShowRainTime)));
        panel.Children.Add(approximateRain);

        var hint = new TextBlock
        {
            Text = "要调整显示天气的城市或图标包，请在应用设置的【天气】页面中配置。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out var brush) && brush is Brush foreground)
        {
            hint.Foreground = foreground;
        }
        panel.Children.Add(hint);

        Content = panel;
    }

    private CheckBox CreateToggle(string label, string property)
    {
        var toggle = new CheckBox { Content = label };
        toggle.SetBinding(ToggleButton.IsCheckedProperty, Bind(property, BindingMode.TwoWay));
        return toggle;
    }

    private ComboBox CreateChoice(string header, string property, string[] choices)
    {
        var combo = new ComboBox
        {
            Header = header,
            ItemsSource = choices,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var selection = Bind(property, BindingMode.TwoWay);
        selection.Converter = new EnumIndexConverter();
        combo.SetBinding(Selector.SelectedIndexProperty, selection);
        return combo;
    }

    private Binding VisibleWhen(string property)
    {
        var binding = Bind(property, BindingMode.OneWay);
        binding.Converter = new BooleanVisibilityConverter();
        return binding;
    }

    private Binding Bind(string property, BindingMode mode) => new()
    {
        Source = Settings,
        Path = new PropertyPath(property),
        Mode = mode
    };
}
