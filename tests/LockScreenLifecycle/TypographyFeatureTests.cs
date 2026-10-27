using Microsoft.Extensions.DependencyInjection;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using OhMyLockScreen.Shared.Services.Scene;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;

internal static class TypographyFeatureTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Typography maps intrinsic font properties to their owning target types", () =>
        {
            using var provider = CreateProvider();
            var service = provider.GetRequiredService<IFeatureService>();
            FrameworkElement[] targets = [new Control(), new ContentPresenter(), new TextBlock(), new RichTextBlock()];
            foreach (var target in targets)
            {
                var settings = new TextFeatureSettings { FontSize = 48, CharacterSpacing = -25, FontStyle = FontStyle.Italic };
                using var text = (TextFeature)service.CreateFeature(new FeatureInfo { Id = TextFeature.Id, Settings = settings })!;
                using var font = (FontFeature)service.CreateFeature(new FeatureInfo
                {
                    Id = FontFeature.Id,
                    Settings = new FontFeatureSettings { FontFamilyName = "Arial", FontWeight = 600 }
                })!;
                text.Attach(target);
                font.Attach(target);
                var properties = TextStyleTargetHelper.GetFontProperties(target)!;
                Check((double)target.GetValue(properties.Size)! == 48
                    && (int)target.GetValue(properties.Spacing)! == -25
                    && (FontStyle)target.GetValue(properties.Style)! == FontStyle.Italic
                    && ((FontFamily)target.GetValue(properties.Family)!).Source == "Arial"
                    && ((FontWeight)target.GetValue(properties.Weight)!).Weight == 600,
                    "Typography was not applied to the target's own properties.");
                settings.FontSize = 72;
                Check((double)target.GetValue(properties.Size)! == 72, "Font size did not update immediately.");
                text.Detach();
                font.Detach();
                Check(ReferenceEquals(target.ReadLocalValue(properties.Size), DependencyProperty.UnsetValue)
                    && ReferenceEquals(target.ReadLocalValue(properties.Family), DependencyProperty.UnsetValue),
                    "Detaching pinned the original effective font values as local values.");
            }
        });

        run("Text feature resolves declared component targets without traversing other descendants", () =>
        {
            using var provider = CreateProvider();
            var settings = new TextFeatureSettings
            {
                LineHeight = 58,
                TextAlignment = TextAlignment.Right,
                Underline = true,
                Strikethrough = true
            };
            using var feature = CreateText(provider, settings);
            var component = new TextTarget();
            var presenter = new ContentPresenter { Content = component };
            feature.Attach(presenter);
            var text = (TextBlock)component.TextElement;
            Check(text.LineHeight == 58 && text.LineStackingStrategy == LineStackingStrategy.BlockLineHeight
                && text.TextAlignment == TextAlignment.Right
                && text.TextDecorations == (TextDecorations.Underline | TextDecorations.Strikethrough),
                "The declared component text target did not receive paragraph styling.");
            settings.LineHeight = 0;
            settings.Underline = false;
            Check(text.LineStackingStrategy == LineStackingStrategy.MaxHeight
                && text.TextDecorations == TextDecorations.Strikethrough,
                "Automatic line height or combined decorations did not update.");
            feature.Attach(new Grid { Children = { text } });
            Check(text.LineHeight == 0 && text.TextAlignment == TextAlignment.Left
                && text.TextDecorations == TextDecorations.None, "Reattachment retained the old target or traversed a grid.");
        });

        run("Typography preserves local values and original bindings across settings replacement", () =>
        {
            using var provider = CreateProvider();
            var originalBinding = new Binding { Source = new object(), Path = new PropertyPath("Size") };
            var target = new TextBlock { FontSize = 19, LineHeight = 26, TextDecorations = TextDecorations.Underline };
            target.SetBinding(TextBlock.FontSizeProperty, originalBinding);
            var oldSettings = new TextFeatureSettings { FontSize = 48, LineHeight = 60 };
            using var feature = CreateText(provider, oldSettings);
            feature.Attach(target);
            var replacement = new TextFeatureSettings { FontSize = 36, Strikethrough = true };
            feature.SetSettings(replacement);
            oldSettings.FontSize = 99;
            Check(target.FontSize == 36, "Replaced settings still controlled the target.");
            feature.Detach();
            Check(ReferenceEquals(target.GetBindingExpression(TextBlock.FontSizeProperty)?.ParentBinding, originalBinding)
                && target.LineHeight == 26 && target.TextDecorations == TextDecorations.Underline,
                "Original binding or local paragraph values were not restored.");
        });

        run("Font inheritance restores the original family whenever selection is cleared", () =>
        {
            using var provider = CreateProvider();
            var settings = new FontFeatureSettings { FontFamilyName = "Arial" };
            using var feature = (FontFeature)provider.GetRequiredService<IFeatureService>()
                .CreateFeature(new FeatureInfo { Id = FontFeature.Id, Settings = settings })!;
            var target = new TextBlock();
            feature.Attach(target);
            settings.FontFamilyName = "Segoe UI";
            Check(target.FontFamily.Source == "Segoe UI", "Changing font families retained the previous override.");
            settings.FontFamilyName = "";
            Check(ReferenceEquals(target.ReadLocalValue(TextBlock.FontFamilyProperty), DependencyProperty.UnsetValue),
                "Selecting inheritance did not clear the font override.");
            var original = new FontFamily("Original");
            target.FontFamily = original;
            settings.FontFamilyName = "Arial";
            settings.FontFamilyName = "";
            Check(ReferenceEquals(target.FontFamily, original), "Clearing selection did not restore a later local value.");
            feature.Attach(new Grid());
            settings.FontFamilyName = "Missing font";
            Check(ReferenceEquals(target.FontFamily, original), "An unsupported target retained the previous font subscription.");
        });

        run("Shared typography settings update independent views and release them on feature removal", () =>
        {
            using var provider = CreateProvider();
            var settings = new TextFeatureSettings();
            var container = new ComponentSettings { Features = [new FeatureInfo { Id = TextFeature.Id, Settings = settings }] };
            var first = new TextBlock { FontSize = 18 };
            var second = new TextBlock();
            var service = provider.GetRequiredService<IFeatureService>();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            using var firstAttacher = new FeatureAttacher(first, service, registry) { FeatureContainer = container };
            using var secondAttacher = new FeatureAttacher(second, service, registry) { FeatureContainer = container };
            firstAttacher.Attach();
            secondAttacher.Attach();
            settings.FontSize = 52;
            settings.Underline = true;
            Check(first.FontSize == 52 && second.FontSize == 52 && second.TextDecorations == TextDecorations.Underline,
                "Independent text views did not follow shared settings.");
            container.Features.Clear();
            settings.FontSize = 99;
            Check(first.FontSize == 18 && ReferenceEquals(second.ReadLocalValue(TextBlock.FontSizeProperty), DependencyProperty.UnsetValue)
                && first.TextDecorations == TextDecorations.None, "Feature removal retained overrides or subscriptions.");
        });

        run("Typography registration shares settings with the matching editor and round trips values", () =>
        {
            using var provider = CreateProvider();
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForCurrentAssembly();
            var service = provider.GetRequiredService<IFeatureService>();
            var info = new FeatureInfo { Id = TextFeature.Id };
            using var feature = (TextFeature)service.CreateFeature(info)!;
            var editor = (TextFeatureSettingsControl)service.CreateSettingsControl(info)!;
            Check(ReferenceEquals(editor.Settings, feature.Settings) && ReferenceEquals(editor.DataContext, feature.Settings),
                "The text editor and feature do not share their observable settings.");
            editor.Settings.Underline = true;
            editor.Settings.CharacterSpacing = -100;
            var restored = ConfigureFileHelper.CopyObject(info).GetSettings<TextFeatureSettings>();
            Check(restored.Underline && restored.CharacterSpacing == -100 && restored.Decorations == TextDecorations.Underline,
                "Text settings did not round trip.");
            info = new FeatureInfo { Id = FontFeature.Id };
            using var font = (FontFeature)service.CreateFeature(info)!;
            var fontEditor = (FontSettingsControl)service.CreateSettingsControl(info)!;
            fontEditor.Settings.FontFamilyName = "Unavailable font";
            fontEditor.Settings.FontWeight = 350;
            var restoredFont = ConfigureFileHelper.CopyObject(info).GetSettings<FontFeatureSettings>();
            Check(ReferenceEquals(fontEditor.Settings, font.Settings) && restoredFont.FontFamilyName == "Unavailable font"
                && restoredFont.FontWeight == 350, "Font settings lost their family name or numeric weight.");
        });

        run("Built-in text defaults retain appearance and are not reapplied after removal", () =>
        {
            var services = new ServiceCollection();
            services.AddComponent<TextComponent>();
            services.AddComponent<ClockComponent>();
            services.AddComponent<DateComponent>();
            using var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<ComponentRegistryService>();
            foreach (var (id, size) in new[] { (TextComponent.Id, 32d), (ClockComponent.Id, 64d), (DateComponent.Id, 24d) })
            {
                var first = registry.Find(id)!.CreateSettings();
                var second = registry.Find(id)!.CreateSettings();
                Check(first.Features.Single(feature => feature.Id == TextFeature.Id).GetSettings<TextFeatureSettings>().FontSize == size
                    && !ReferenceEquals(first.Features[0].Settings, second.Features[0].Settings),
                    "Built-in default typography changed or shares mutable settings.");
                first.Features.Clear();
                Check(ConfigureFileHelper.CopyObject(first).Features.Count == 0, "Saving and loading restored removed defaults.");
            }

            var scene = DefaultSceneFactory.Create("Typography");
            var children = scene.Layers[0].Children[0].GetSettings<OhMyLockScreen.Models.Scene.ContainerComponentSettings>().Children;
            Check(children[0].Features.Single(feature => feature.Id == FontFeature.Id).GetSettings<FontFeatureSettings>().FontWeight == 700
                && children[1].Features.Single(feature => feature.Id == TextFeature.Id).GetSettings<TextFeatureSettings>().FontSize == 24,
                "Default scenes do not use the shared clock and date typography defaults.");
        });
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddFeature<TextFeature, TextFeatureSettingsControl>();
        services.AddFeature<FontFeature, FontSettingsControl>();
        return services.BuildServiceProvider();
    }

    private static TextFeature CreateText(IServiceProvider provider, TextFeatureSettings settings) =>
        (TextFeature)provider.GetRequiredService<IFeatureService>()
            .CreateFeature(new FeatureInfo { Id = TextFeature.Id, Settings = settings })!;

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private sealed class TextTarget : UserControl, ITextStyleTarget
    {
        public FrameworkElement TextElement { get; } = new TextBlock();
    }
}
