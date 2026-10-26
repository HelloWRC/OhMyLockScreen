using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;

internal static class ForegroundFeatureTests
{
    private const string FeatureId = "omls.features.appearance.foreground";

    internal static void Run(Action<string, Action> run)
    {
        run("Foreground synchronizes the owning property of every supported framework element type", () =>
        {
            (FrameworkElement Target, DependencyProperty Property)[] cases =
            [
                (new UserControl(), Control.ForegroundProperty),
                (new TextBlock(), TextBlock.ForegroundProperty),
                (new RichTextBlock(), RichTextBlock.ForegroundProperty),
                (new ContentPresenter(), ContentPresenter.ForegroundProperty)
            ];
            foreach (var (target, property) in cases)
            {
                var original = new SolidColorBrush();
                target.SetValue(property, original);
                var settings = new ForegroundFeatureSettings();
                using var feature = new ForegroundFeature();
                feature.SetSettings(settings);
                feature.Attach(target);
                Check(target.GetValue(property) == null, "The initial null foreground was not applied.");
                var gradient = new LinearGradientBrush();
                settings.Foreground = gradient;
                Check(ReferenceEquals(target.GetValue(property), gradient), "A replacement brush did not update the target.");
                settings.Foreground = null;
                Check(target.GetValue(property) == null, "Clearing the foreground did not update the target.");
                feature.Detach();
                feature.Detach();
                settings.Foreground = gradient;
                Check(ReferenceEquals(target.GetValue(property), original) && feature.Target == null,
                    "Detaching did not restore the original foreground or release its subscription.");
            }
        });

        run("Elements without a foreground accept attachment without modifying their descendants", () =>
        {
            var original = new SolidColorBrush();
            var child = new TextBlock { Foreground = original };
            var grid = new Grid();
            grid.Children.Add(child);
            var settings = new ForegroundFeatureSettings { Foreground = new LinearGradientBrush() };
            using var feature = new ForegroundFeature();
            feature.SetSettings(settings);
            feature.Attach(new FrameworkElement());
            feature.Attach(grid);
            settings.Foreground = new SolidColorBrush();
            Check(ReferenceEquals(feature.Target, grid) && ReferenceEquals(child.Foreground, original),
                "A target without a foreground changed descendant text or rejected attachment.");
            feature.Detach();
            Check(feature.Target == null && ReferenceEquals(child.Foreground, original), "Detaching an unsupported target changed its descendants.");
        });

        run("Intrinsic foreground properties reject unrelated owners as they do in native UWP", () =>
        {
            foreach (var target in new FrameworkElement[] { new Grid(), new UserControl(), new TextBlock(), new ContentPresenter() })
            {
                try
                {
                    target.GetValue(TextElement.ForegroundProperty);
                    throw new Exception("TextElement.Foreground was incorrectly treated as an attached property.");
                }
                catch (COMException)
                {
                }
            }
        });

        run("Foreground stays on a presenter instead of redirecting to its decoration border", () =>
        {
            var target = new ComponentPresenter();
            var border = new Border();
            target.LoadDecorationBorder(border);
            var settings = new ForegroundFeatureSettings { Foreground = new SolidColorBrush() };
            using var feature = new ForegroundFeature();
            feature.SetSettings(settings);
            feature.Attach(target);
            Check(ReferenceEquals(target.Foreground, settings.Foreground),
                "Foreground target resolution redirected to the decoration border.");
        });

        run("Foreground settings replacement and reattachment release obsolete settings and targets", () =>
        {
            var firstSettings = new ForegroundFeatureSettings { Foreground = new SolidColorBrush() };
            var secondSettings = new ForegroundFeatureSettings { Foreground = new LinearGradientBrush() };
            var controlOriginal = new SolidColorBrush();
            var first = new UserControl { Foreground = controlOriginal };
            var second = new TextBlock();
            using var feature = new ForegroundFeature();
            feature.SetSettings(firstSettings);
            feature.Attach(first);
            feature.SetSettings(secondSettings);
            firstSettings.Foreground = null;
            Check(ReferenceEquals(first.Foreground, secondSettings.Foreground), "Old settings still controlled a foreground property.");
            feature.Attach(second);
            Check(ReferenceEquals(first.Foreground, controlOriginal)
                && ReferenceEquals(second.Foreground, secondSettings.Foreground),
                "Reattachment did not restore the old target and update the new one.");
            feature.Dispose();
            secondSettings.Foreground = new SolidColorBrush();
            Check(second.Foreground == null && feature.Target == null,
                "Disposal retained the new target or its subscription.");
        });

        run("Removing a foreground feature restores independent views sharing the same settings", () =>
        {
            using var provider = CreateProvider();
            var settings = new ForegroundFeatureSettings { Foreground = new SolidColorBrush() };
            var container = new ComponentSettings
            {
                Features = [new FeatureInfo { Id = FeatureId, Settings = settings }]
            };
            var original = new SolidColorBrush();
            var first = new Control { Foreground = original };
            var second = new RichTextBlock();
            var service = provider.GetRequiredService<IFeatureService>();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            using var firstAttacher = new FeatureAttacher(first, service, registry) { FeatureContainer = container };
            using var secondAttacher = new FeatureAttacher(second, service, registry) { FeatureContainer = container };
            firstAttacher.Attach();
            secondAttacher.Attach();
            var replacement = new LinearGradientBrush();
            settings.Foreground = replacement;
            Check(ReferenceEquals(first.Foreground, replacement)
                && ReferenceEquals(second.Foreground, replacement),
                "Independent views did not synchronize the shared foreground settings.");
            container.Features.Clear();
            settings.Foreground = new SolidColorBrush();
            Check(ReferenceEquals(first.Foreground, original) && second.Foreground == null,
                "Feature removal did not release all view bindings.");
        });

        run("Foreground registration supplies a matching editor and shared observable settings", () =>
        {
            using var provider = CreateProvider();
            var service = provider.GetRequiredService<IFeatureService>();
            var info = new FeatureInfo { Id = FeatureId };
            using var feature = (ForegroundFeature)service.CreateFeature(info)!;
            var editor = (ForegroundSettingsControl)service.CreateSettingsControl(info)!;
            Check(ReferenceEquals(editor.DataContext, feature.Settings) && ReferenceEquals(editor.Settings, feature.Settings)
                && feature.Settings.Foreground == null, "Registration did not supply shared default settings and the matching editor.");
            var target = new Control();
            feature.Attach(target);
            var brush = new LinearGradientBrush();
            editor.Settings.Foreground = brush;
            Check(ReferenceEquals(target.Foreground, brush),
                "Editor settings did not update the attached feature.");
        });
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddFeature<ForegroundFeature, ForegroundSettingsControl>();
        return services.BuildServiceProvider();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
