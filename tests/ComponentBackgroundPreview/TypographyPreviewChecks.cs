using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Services.Scene;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async Task VerifyTypographyAsync(StackPanel root)
    {
        root.Children.Clear();
        root.Children.Add(_status);
        _status.Text = "文本与字体特性预览";
        root.RequestedTheme = ElementTheme.Light;
        root.Background = new SolidColorBrush(Colors.White);
        var service = IAppHost.GetRequiredService<IFeatureService>();
        var registry = IAppHost.GetRequiredService<ComponentRegistryService>();
        var model = registry.Find(TextComponent.Id)!.CreateSettings();
        model.GetSettings<TextComponentSettings>().Text = "文本样式预览\nAa Bb 012345\n中文字符间距";
        var textInfo = model.Features.Single();
        var textSettings = textInfo.GetSettings<TextFeatureSettings>();
        textSettings.FontSize = 36;
        textSettings.LineHeight = 64;
        textSettings.CharacterSpacing = -25;
        textSettings.TextAlignment = TextAlignment.Right;
        textSettings.FontStyle = FontStyle.Italic;
        textSettings.Underline = true;
        textSettings.Strikethrough = true;
        var fontInfo = new FeatureInfo
        {
            Id = FontFeature.Id,
            Settings = new FontFeatureSettings { FontFamilyName = "Consolas", FontWeight = 600 }
        };
        model.Features.Add(fontInfo);
        var fontSettings = fontInfo.GetSettings<FontFeatureSettings>();
        var presenter = new ComponentPresenter { Settings = model, Width = 340 };
        var scope = new UserControl
        {
            FontSize = 20,
            FontFamily = new FontFamily("Arial"),
            Content = presenter,
            Foreground = new SolidColorBrush(Colors.Black),
            VerticalAlignment = VerticalAlignment.Top
        };
        var gallery = new Grid { ColumnSpacing = 16, Margin = new Thickness(16) };
        for (var index = 0; index < 3; index++)
        {
            gallery.ColumnDefinitions.Add(new ColumnDefinition());
        }

        var textEditor = (TextFeatureSettingsControl)service.CreateSettingsControl(textInfo)!;
        var fontEditor = (FontSettingsControl)service.CreateSettingsControl(fontInfo)!;
        Grid.SetColumn(textEditor, 1);
        Grid.SetColumn(fontEditor, 2);
        gallery.Children.Add(scope);
        gallery.Children.Add(textEditor);
        gallery.Children.Add(fontEditor);
        root.Children.Add(gallery);
        root.UpdateLayout();
        await Task.Delay(200);
        var component = (TextComponent)presenter.Content;
        var text = (TextBlock)component.TextElement;
        Check(text.FontSize == 36 && text.CharacterSpacing == -25 && text.FontStyle == FontStyle.Italic
            && text.FontFamily.Source == "Consolas" && text.FontWeight.Weight == 600,
            "Native font properties inherit through the presenter template and actual text component");
        Check(text.LineHeight == 64 && text.LineStackingStrategy == LineStackingStrategy.BlockLineHeight
            && text.TextAlignment == TextAlignment.Right
            && text.TextDecorations == (TextDecorations.Underline | TextDecorations.Strikethrough),
            "Native text targets receive line height, alignment and combined decorations");

        var numbers = Descendants(textEditor).OfType<Microsoft.UI.Xaml.Controls.NumberBox>()
            .ToDictionary(input => input.Header.ToString()!);
        numbers["字号"].Value = 42;
        numbers["字符间距"].Value = -100;
        numbers["行高"].Value = 72;
        Check(textSettings.FontSize == 42 && text.FontSize == 42 && text.CharacterSpacing == -100 && text.LineHeight == 72,
            "Native typography NumberBox edits update settings and rendered text");
        numbers["字号"].Value = double.NaN;
        Check(textSettings.FontSize == 42, "Clearing the native size input preserves the saved font size");
        numbers["字号"].Value = 42;
        var combos = Descendants(textEditor).OfType<ComboBox>().ToDictionary(combo => combo.Header.ToString()!);
        combos["文本对齐"].SelectedIndex = 1;
        combos["字体样式"].SelectedIndex = 0;
        Check(textSettings.TextAlignment == TextAlignment.Center && text.TextAlignment == TextAlignment.Center
            && text.FontStyle == FontStyle.Normal, "Native enum ComboBox selections write the correct values");
        var underline = Descendants(textEditor).OfType<CheckBox>().Single(input => input.Content as string == "下划线");
        underline.IsChecked = false;
        Check(!textSettings.Underline && text.TextDecorations == TextDecorations.Strikethrough,
            "Native decoration toggles update the combined text decoration flags");
        underline.IsChecked = true;
        var fontCombos = Descendants(fontEditor).OfType<ComboBox>().ToDictionary(combo => combo.Header.ToString()!);
        fontCombos["字重"].SelectedIndex = 7;
        Check(fontSettings.FontWeight == 700 && text.FontWeight.Weight == 700, "Native font weight selection writes a numeric weight");
        var fontCombo = fontCombos["字体"];
        Check(fontEditor.FontFamilies.Count > 2 && fontCombo.SelectedValue as string == "Consolas",
            "The font picker enumerates installed fonts and selects the saved font");
        fontCombo.SelectedIndex = 0;
        Check(fontSettings.FontFamilyName == "" && text.FontFamily.Source == "Arial", "Selecting font inheritance restores the parent font");
        fontSettings.FontFamilyName = "Consolas";
        fontCombo.IsDropDownOpen = true;
        await Task.Delay(150);
        var item = (ComboBoxItem?)fontCombo.ContainerFromItem(fontEditor.FontFamilies.Single(choice => choice.Name == "Consolas"));
        var label = item == null ? null : Descendants(item).OfType<TextBlock>().FirstOrDefault();
        Check(label?.FontFamily.Source == "Consolas" && ToolTipService.GetToolTip(label) as string == "Consolas",
            "Font options render in their own family and expose the font name tooltip");
        await CaptureAsync(root, "typography-fonts.png");
        fontCombo.IsDropDownOpen = false;
        await Task.Delay(100);
        await CaptureAsync(root, "typography-editor.png");

        model.Features.Remove(textInfo);
        scope.FontSize = 28;
        Check(text.FontSize == 28 && text.LineHeight == 0 && text.TextDecorations == TextDecorations.None,
            "Removing the text feature restores native inheritance and original paragraph values");
        scope.FontSize = 30;
        Check(text.FontSize == 30, "Inherited typography remains responsive after feature removal");
        model.Features.Remove(fontInfo);
        scope.FontFamily = new FontFamily("Segoe UI");
        Check(text.FontFamily.Source == "Segoe UI", "Removing the font feature restores responsive native font inheritance");

        var bindingSource = new TextFeatureSettings { FontSize = 18 };
        text.SetBinding(TextBlock.FontSizeProperty, new Binding
        {
            Source = bindingSource,
            Path = new PropertyPath(nameof(TextFeatureSettings.FontSize)),
            Mode = BindingMode.OneWay
        });
        using (var overrideFeature = (TextFeature)service.CreateFeature(new FeatureInfo { Id = TextFeature.Id })!)
        {
            overrideFeature.Attach(text);
            Check(text.FontSize == 32, "A text feature can temporarily override a native XAML binding");
        }

        bindingSource.FontSize = 25;
        Check(text.FontSize == 25 && text.GetBindingExpression(TextBlock.FontSizeProperty) != null,
            "Detaching restores the original native binding and its subsequent notifications");
        text.ClearValue(TextBlock.FontSizeProperty);
        model.Features.Add(textInfo);
        model.Features.Add(fontInfo);
        var clockModel = registry.Find(ClockComponent.Id)!.CreateSettings();
        var clockPresenter = new ComponentPresenter { Settings = clockModel };
        root.Children.Add(clockPresenter);
        await Task.Delay(100);
        var clock = (TextBlock)((ClockComponent)clockPresenter.Content).TextElement;
        Check(clock.FontSize == 64 && clock.FontWeight.Weight == 700, "A newly created native clock retains its default size and weight");
        root.Children.Remove(clockPresenter);
    }
}
