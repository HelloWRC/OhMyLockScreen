using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.Scene;
using OhMyLockScreen.ViewModels;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace ComponentBackgroundPreview;

public sealed partial class ThemePreviewPage : Page
{
    public ThemePreviewPage() => InitializeComponent();
}

public sealed partial class App
{
    private async void LaunchThemePreview()
    {
        var report = Path.Combine(ApplicationData.Current.LocalFolder.Path, "theme-report.txt");
        File.WriteAllText(report, "RUNNING");
        _report.Clear();
        try
        {
            var frame = new Frame { RequestedTheme = ElementTheme.Light };
            frame.Navigate(typeof(ThemePreviewPage));
            var page = (ThemePreviewPage)frame.Content;
            var scene = new SceneSettings();
            var text = IAppHost.GetRequiredService<ComponentRegistryService>().Find(TextComponent.Id)!;
            var first = text.CreateSettings();
            scene.Layers = [new LayerSettings { Children = [first, text.CreateSettings()] }];
            page.Surface.Scene = scene;
            var brightImage = await CreateSyntheticPreviewImageAsync("theme-bright.png", 356, 200);
            var darkImage = await CreateSyntheticPreviewImageAsync("theme-dark.png", 356, 200, dark: true);
            page.Card.Item = new SceneCardItem(Guid.NewGuid())
            {
                Name = "主题预览",
                Author = "示例作者",
                PreviewImageUri = brightImage
            };
            Window.Current.Content = frame;
            Window.Current.Activate();
            await Task.Delay(200);

            var sceneTheme = new FeatureInfo { Id = "omls.features.appearance.theme", Settings = new ThemeFeatureSettings() };
            var service = IAppHost.GetRequiredService<IFeatureService>();
            var editor = service.CreateSettingsControl(sceneTheme)!;
            page.EditorHost.Content = editor;
            page.UpdateLayout();
            var picker = FindDropVisual<ComboBox>(editor)!;
            Check(picker.SelectedIndex == 0, "Theme editor starts at Default");
            var texts = Descendants(page.Surface).OfType<TextComponent>().Select(control => (TextBlock)control.Content).ToArray();
            Check(texts.Length == 2 && texts.All(t => ((SolidColorBrush)t.Foreground).Color.R > 200)
                && page.Surface.ActualTheme == ElementTheme.Dark, "A scene in a light page initially renders light text on its dark theme");
            scene.Features.Add(sceneTheme);
            picker.SelectedIndex = 1;
            await Task.Delay(100);
            Check(sceneTheme.GetSettings<ThemeFeatureSettings>().RequestedTheme == ElementTheme.Light
                && page.Surface.ActualTheme == ElementTheme.Light && texts.All(t => ((SolidColorBrush)t.Foreground).Color.R < 80),
                "Picking Light overrides the scene and darkens its default text");
            first.Features.Add(new FeatureInfo { Id = sceneTheme.Id, Settings = new ThemeFeatureSettings { RequestedTheme = ElementTheme.Dark } });
            await Task.Delay(100);
            Check(texts.Count(t => t.ActualTheme == ElementTheme.Dark) == 1 && page.Surface.ActualTheme == ElementTheme.Light,
                "A component override affects only its subtree");
            sceneTheme.IsEnabled = false;
            await Task.Delay(100);
            Check(page.Surface.RequestedTheme == ElementTheme.Dark && texts.All(t => t.ActualTheme == ElementTheme.Dark),
                "Disabling the scene override restores its original dark theme");

            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                frame.RequestedTheme = theme;
                await Task.Delay(120);
                var card = page.Card;
                card.SetInteractionStateForTesting(isPointerOver: true, isPressed: false, useTransitions: false);
                var button = (Button)card.FindName("EditButton");
                var light = theme == ElementTheme.Light;
                var tint = card.BackdropBrush.TintColor;
                Check(page.ActualTheme == theme && card.HeaderOverlayElement.ActualTheme == theme && button.ActualTheme == theme
                    && tint.A == 0xA3 && tint.R == (light ? 255 : 0)
                    && (((SolidColorBrush)button.Foreground).Color.R < 80) == light, $"Card overlay and actions follow the {theme} host");
                foreach (var image in new[] { brightImage, darkImage })
                {
                    card.Item.PreviewImageUri = image;
                    await Task.Delay(150);
                    await CaptureAsync(card, $"card-{theme}-{Path.GetFileName(image.LocalPath)}");
                }
                using var feature = new ThemeFeature();
                feature.SetSettings(new ThemeFeatureSettings { RequestedTheme = light ? ElementTheme.Dark : ElementTheme.Light });
                feature.Attach(button);
                Check(button.ActualTheme != theme && card.ActualTheme == theme, "An ordinary button supports an independent override");
                feature.Detach();
                Check(button.ActualTheme == theme, "Detaching restores the inherited button theme");
                var showing = page.Dialog.ShowAsync();
                await Task.Delay(180);
                Check(page.Dialog.ActualTheme == theme, $"Dialog follows the {theme} host");
                page.Dialog.Hide();
                await showing;
            }
            File.WriteAllLines(report, _report);
        }
        catch (Exception exception)
        {
            File.WriteAllLines(report, _report.Append("FAIL: " + exception));
        }
        finally
        {
            Exit();
        }
    }
}
