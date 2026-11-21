using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Controls.Rules;
using OhMyLockScreen.Models.Rules;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Rules;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services;
using OhMyLockScreen.Shared.Abstractions.Services.Weather;
using OhMyLockScreen.Shared.Models.Weather;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.Ruleset;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Registry;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Media;
using OhMyLockScreen.Shared.Models.Ruleset;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Models.Features.Visibility;
using OhMyLockScreen.Services.Features.Visibility;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private static void InvokeRuleButton(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private async void LaunchRulesetPreview()
    {
        var local = ApplicationData.Current.LocalFolder.Path;
        var report = Path.Combine(local, "ruleset-report.txt");
        File.WriteAllText(report, "RUNNING");
        try
        {
            var playing = true;
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IRulesetService, RulesetService>();
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddComponent<TextComponent>();
                services.AddFeature<HideByRulesFeature, HideByRulesSettingsControl>();
                services.AddWeatherIconTemplate(WeatherIconIds.Lucide, "Lucide", "LucideWeatherIconTemplate");
                services.AddSingleton<IWeatherService>(_ => new WeatherService(
                    new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(local, "RuleWeatherSettings.json")),
                    new System.Net.Http.HttpClient(), TimeProvider.System, NullLogger<WeatherService>.Instance));
                services.AddRule<CurrentWeatherRuleSettings, CurrentWeatherRuleControl>(BuiltinRules.WeatherId, "当前天气是");
                services.AddRule<RainTimeRuleSettings, RainTimeRuleControl>(BuiltinRules.RainId, "距离降水开始/结束还剩", BuiltinRules.RainIcon);
                services.AddRule(BuiltinRules.PlayingId, "有媒体正在播放", "\uE768", _ => playing);
                services.AddRule<MediaFieldRuleSettings, MediaFieldRuleControl>(BuiltinRules.FieldId, "媒体字段是", "\uE8D6", _ => false);
            }).Build();
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(SceneSettings).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(App).Assembly);
            var path = Path.Combine(local, "RulesetPreview");
            Directory.CreateDirectory(path);
            using var settingsService = new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(path, "Settings.json"));
            using var scenes = new SceneService(settingsService, NullLogger<SceneService>.Instance, path);
            var seed = new SceneSettings { SceneId = new Guid("03580C73-A4D8-4AA5-8029-1C8A2883E7D0"), Layers = [new()] };
            var scenePath = Path.Combine(scenes.ScenesPath, seed.SceneId.ToString("N"));
            Directory.CreateDirectory(scenePath);
            ConfigureFileHelper.SaveConfig(Path.Combine(scenePath, "scene.json"), seed);
            scenes.Initialize();
            var scene = scenes.Load(scenes.ActiveSceneId);
            var settings = new HideByRulesSettings();
            var rule = settings.Ruleset.Groups[0].Rules[0];
            rule.Id = BuiltinRules.PlayingId;
            var feature = new FeatureInfo { Id = "omls.features.visibility.hideByRules", Settings = settings };
            var model = new ComponentSettings { Id = TextComponent.Id, Settings = new TextComponentSettings(), Features = [feature] };
            scene.Layers[0].Children = [model];
            scenes.QueueSave(scene);
            using var graph = new ObservableSettingsGraph(scene, (_, _, _) => scenes.QueueSave(scene));
            var host = new RulesetPreviewEditor();
            var presenter = host.PreviewPresenter;
            presenter.Settings = model;
            var panel = IAppHost.GetRequiredService<IFeatureService>().CreateSettingsControl(feature)!;
            host.FeatureEditor.Content = panel;
            Window.Current.Content = host;
            Window.Current.Activate();
            await Task.Delay(300);
            await VerifyRuleStripesAsync(presenter, value =>
            {
                playing = value;
                IAppHost.GetRequiredService<IRulesetService>().NotifyStatusChanged();
            });
            var button = Descendants(panel).OfType<Button>().Single();
            void Open() => InvokeRuleButton(button);
            Open();
            await Task.Delay(500);
            var editor = (RulesetControl)host.Drawer.DrawerContent;
            Check(host.Drawer.IsDrawerOpen && editor.ActualWidth == 600 && presenter.Visibility == Visibility.Visible, "Feature button opens a 600 DIP editor; editing keeps component visible");
            foreach (var theme in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                host.RequestedTheme = theme;
                await Task.Delay(250);
                var surface = Descendants(host.Drawer).OfType<Border>().Single(border => border.Name == "PART_DrawerContentBorder");
                await CaptureAsync(surface, $"ruleset-{theme}.png");
            }
            var selectors = Descendants(editor).OfType<CommunityToolkit.WinUI.Controls.Segmented>().ToArray();
            Check(selectors.Length == 2 && selectors[0].SelectedIndex == 0 && selectors[1].SelectedIndex == 1,
                "Native Segmented selectors preserve root OR and group AND");
            foreach (var selector in selectors)
            {
                var before = selector.SelectedIndex;
                var viewports = selector.Items.Cast<CommunityToolkit.WinUI.Controls.SegmentedItem>()
                    .Select(item => Descendants(item).OfType<Canvas>().Single(canvas => canvas.Name.EndsWith("Viewport"))).ToArray();
                Check(viewports[before].Width > 0 && viewports[1 - before].Width == 0, "Only the selected label is expanded");
                Check(((FrameworkElement)selector.Items[1 - before]).ActualWidth < 60, "Collapsed segment takes icon width, not the width of the selected segment");
                selector.SelectedIndex = 1 - before;
                await Task.Delay(110);
                if (new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
                {
                    var expanding = viewports[1 - before];
                    var label = (TextBlock)expanding.Children[0];
                    Check(expanding.Width > 0 && label.Opacity is > 0 and < 1, "Selection animates label width and opacity");
                }
                await Task.Delay(450);
                Check(viewports[before].Width == 0 && viewports[1 - before].Width > 0, "Deselected label collapses to icon only");
                selector.SelectedIndex = before;
                await Task.Delay(450);
            }
            var typePicker = Descendants(editor).OfType<ComboBox>().Single();
            var registry = IAppHost.GetRequiredService<IRulesetService>().Rules;
            typePicker.SelectedItem = registry[BuiltinRules.WeatherId];
            await Task.Delay(150);
            var weatherEditor = Descendants(editor).OfType<CurrentWeatherRuleControl>().Single();
            var weatherPicker = Descendants(weatherEditor).OfType<ComboBox>().Single();
            weatherPicker.SelectedIndex = 1;
            Descendants(weatherEditor).OfType<CheckBox>().Single().IsChecked = true;
            Check(rule.Settings is CurrentWeatherRuleSettings { IsFuzzyMatch: true } weather && weather.WeatherId == ((WeatherRuleChoice)weatherPicker.SelectedItem).Code,
                "Weather picker and fuzzy checkbox update typed settings");
            typePicker.IsDropDownOpen = true;
            await Task.Delay(250);
            var rainItem = (ComboBoxItem)typePicker.ContainerFromItem(registry[BuiltinRules.RainId]);
            Check(Descendants(rainItem).OfType<ViewFluentIcon>().Any(icon => icon.Icon == FluentIcons.Common.Icon.WeatherRain &&
                icon.Visibility == Visibility.Visible && (icon.IconSize is FluentIcons.Common.IconSize.Size20 or FluentIcons.Common.IconSize.Resizable) &&
                icon.FontSize == 20 && icon.IconVariant == FluentIcons.Common.IconVariant.Regular),
                "Rain rule dropdown uses ic_fluent_weather_rain_20_regular: " + string.Join(", ", Descendants(rainItem).OfType<ViewFluentIcon>().Select(i => $"{i.Icon}/{i.IconSize}/{i.IconVariant}")));
            await CaptureAsync(rainItem, "ruleset-rain-item.png");
            typePicker.IsDropDownOpen = false;
            typePicker.SelectedItem = registry[BuiltinRules.RainId];
            await Task.Delay(150);
            var rainEditor = Descendants(editor).OfType<RainTimeRuleControl>().Single();
            Descendants(rainEditor).OfType<CommunityToolkit.WinUI.Controls.Segmented>().Single().SelectedIndex = 1;
            Descendants(rainEditor).OfType<Microsoft.UI.Xaml.Controls.NumberBox>().Single().Value = 12;
            await Task.Delay(80);
            Check(rule.Settings is RainTimeRuleSettings { IsRemainingTime: true, RainTimeMinutes: 12 }, "Rain segment and threshold update typed settings");
            var group = settings.Ruleset.Groups[0];
            InvokeRuleButton(Descendants(editor).OfType<Button>().Single(b => Equals(ToolTipService.GetToolTip(b), "复制")));
            await Task.Delay(150);
            Check(settings.Ruleset.Groups.Count == 2 && !ReferenceEquals(group.Rules[0].Settings, settings.Ruleset.Groups[1].Rules[0].Settings), "Copy group creates independent rule settings");
            InvokeRuleButton(Descendants(editor).OfType<Button>().Single(b => b.DataContext == settings.Ruleset.Groups[1] && Equals(ToolTipService.GetToolTip(b), "删除")));
            await Task.Delay(150);
            Check(settings.Ruleset.Groups.Count == 1, "Delete removes only the selected group");
            typePicker.SelectedItem = registry[BuiltinRules.FieldId];
            await Task.Delay(150);
            var fieldEditor = Descendants(editor).OfType<MediaFieldRuleControl>().Single();
            Descendants(fieldEditor).OfType<ComboBox>().Single().SelectedIndex = (int)MediaTextField.Artist;
            ((StackPanel)fieldEditor.Content).Children.OfType<TextBox>().Single().Text = "^Artist$";
            Descendants(fieldEditor).OfType<CheckBox>().Single().IsChecked = true;
            await Task.Delay(150);
            scenes.FlushPending();
            var saved = scenes.Load(scene.SceneId).Layers[0].Children[0].Features[0].GetSettings<HideByRulesSettings>();
            var restored = System.Text.Json.JsonSerializer.Deserialize<MediaFieldRuleSettings>(((System.Text.Json.JsonElement)saved.Ruleset.Groups[0].Rules[0].Settings!).GetRawText());
            Check(restored is { Field: MediaTextField.Artist, UseRegex: true, Text: "^Artist$" }, "Inline field, regex and typed value persisted through scene storage");
            var settingsRow = (StackPanel)fieldEditor.Content;
            var regexBox = settingsRow.Children.OfType<CheckBox>().Single();
            var editorRow = Descendants(editor).OfType<ScrollViewer>().Single(view => view.Content is StackPanel stack && stack.Children.OfType<RuleOptionsPresenter>().Any());
            host.Width = 350;
            editor.Width = 350;
            await Task.Delay(150);
            editorRow.ChangeView(editorRow.ScrollableWidth, null, null, true);
            await Task.Delay(100);
            Check(editorRow.ScrollableWidth > 0 && editorRow.HorizontalOffset > 0 && regexBox.IsChecked == true, "Narrow rule row can scroll to field settings");
            var narrowSurface = Descendants(host.Drawer).OfType<Border>().Single(border => border.Name == "PART_DrawerContentBorder");
            await CaptureAsync(narrowSurface, "ruleset-narrow.png");
            host.Drawer.IsDrawerOpen = false;
            host.Drawer.DrawerContent = null;
            await Task.Delay(300);
            Open();
            await Task.Delay(350);
            var reopened = (RulesetControl)host.Drawer.DrawerContent;
            Check(Descendants(reopened).OfType<MediaFieldRuleControl>().Any(), "Drawer reopens with the selected rule editor");
            host.Drawer.IsDrawerOpen = false;
            host.Drawer.DrawerContent = null;
            rule.Id = BuiltinRules.PlayingId;
            rule.Settings = null;
            presenter.IsEditing = false;
            await Task.Delay(100);
            Check(presenter.Visibility == Visibility.Collapsed, "Satisfied rule hides preview presenter");
            playing = false;
            IAppHost.GetRequiredService<IRulesetService>().NotifyStatusChanged();
            await Task.Delay(100);
            Check(presenter.Visibility == Visibility.Visible, "Hidden presenter restores on state change");
            ((FrameworkElement)presenter.Content).Visibility = Visibility.Collapsed;
            model.Features.Clear();
            Check(presenter.Visibility == Visibility.Collapsed, "Removing rule preserves component's own visibility");
            File.WriteAllLines(report, _report);
        }
        catch (Exception exception) { File.AppendAllText(report, "\nFAIL: " + exception); }
        finally { Exit(); }
    }
}
