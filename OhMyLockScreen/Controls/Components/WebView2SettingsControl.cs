using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Controls.Components;

public sealed class WebView2SettingsControl : ControlWithSettingsBase<WebView2ComponentSettings>
{
    protected override void OnSettingsChanged()
    {
        var settings = Settings;
        var address = new TextBox { Header = "网页地址", PlaceholderText = "https://example.com" };
        var error = new TextBlock
        {
            Text = "请输入完整的 HTTP 或 HTTPS 地址。",
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        address.TextChanged += (_, _) =>
        {
            error.Visibility = !string.IsNullOrWhiteSpace(address.Text)
                && WebView2ComponentSettings.ParseAddress(address.Text) == null
                ? Visibility.Visible
                : Visibility.Collapsed;
        };
        address.SetBinding(TextBox.TextProperty, new Binding
        {
            Path = new PropertyPath(nameof(Settings.Uri)),
            Source = settings,
            Mode = BindingMode.OneWay
        });
        address.LostFocus += (_, _) => settings.Uri = address.Text;

        var allowInteraction = new CheckBox { Content = "允许与页面交互" };
        allowInteraction.SetBinding(ToggleButton.IsCheckedProperty, new Binding
        {
            Path = new PropertyPath(nameof(Settings.AllowInteraction)),
            Source = settings,
            Mode = BindingMode.TwoWay
        });

        var panel = new StackPanel { Style = (Style)Application.Current.Resources["EditorFormStyle"] };
        panel.Children.Add(address);
        panel.Children.Add(error);
        panel.Children.Add(allowInteraction);
        Content = panel;
    }
}
