using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

internal static class BackgroundFeatureTests
{
    private const string FeatureId = "omls.features.appearance.background";

    internal static void Run(Action<string, Action> run)
    {
        run("Background target resolution shares the actual painted control with gradient editing", () =>
        {
            var control = new Control();
            var border = new Border();
            var presenter = new ComponentPresenter();
            Check(BackgroundFeature.ResolveBackgroundTarget(presenter) == null, "An unrealized presenter exposed a painted target.");
            presenter.LoadDecorationBorder(border);
            Check(ReferenceEquals(BackgroundFeature.ResolveBackgroundTarget(control), control)
                && ReferenceEquals(BackgroundFeature.ResolveBackgroundTarget(presenter), border),
                "Background editing did not resolve the painted control.");
            presenter.UnloadDecorationBorder(border);
            Check(BackgroundFeature.ResolveBackgroundTarget(presenter) == null
                && BackgroundFeature.ResolveBackgroundTarget(new ContentPresenter { Content = new UserControl() }) == null
                && BackgroundFeature.ResolveBackgroundTarget(new FrameworkElement()) == null
                && BackgroundFeature.ResolveBackgroundTarget(null) == null,
                "An unsupported host or absent decoration border was treated as a painted target.");
        });

        run("Background changes and null synchronize immediately and detach restores the original brush", () =>
        {
            var original = new SolidColorBrush(Color.FromArgb(255, 10, 20, 30));
            var settings = new BackgroundFeatureSettings();
            var target = new Control { Background = original };
            using var feature = new BackgroundFeature();
            feature.SetSettings(settings);
            feature.Attach(target);
            Check(target.Background == null, "The default null background was not applied.");
            var gradient = new LinearGradientBrush();
            settings.Background = gradient;
            Check(ReferenceEquals(target.Background, gradient), "A replacement brush did not update the target.");
            settings.Background = null;
            Check(target.Background == null, "Clearing the background did not update the target.");
            feature.Detach();
            feature.Detach();
            settings.Background = gradient;
            Check(ReferenceEquals(target.Background, original) && feature.Target == null,
                "Detaching did not restore the original brush or release the subscription.");
        });

        run("Background settings replacement and reattachment release old settings and targets", () =>
        {
            var firstBrush = new SolidColorBrush();
            var secondBrush = new LinearGradientBrush();
            var firstSettings = new BackgroundFeatureSettings { Background = firstBrush };
            var secondSettings = new BackgroundFeatureSettings { Background = secondBrush };
            var original = new SolidColorBrush();
            var first = new UserControl { Background = original };
            var second = new Border();
            var presenter = new ComponentPresenter();
            presenter.LoadDecorationBorder(second);
            using var feature = new BackgroundFeature();
            feature.SetSettings(firstSettings);
            feature.Attach(first);
            feature.SetSettings(secondSettings);
            firstSettings.Background = null;
            Check(ReferenceEquals(first.Background, secondBrush), "Old settings still controlled the background.");
            feature.Attach(presenter);
            Check(ReferenceEquals(first.Background, original) && ReferenceEquals(second.Background, secondBrush),
                "Reattachment did not restore the old target and update the new content.");
            feature.Dispose();
            secondSettings.Background = firstBrush;
            Check(second.Background == null && ReferenceEquals(first.Background, original),
                "Disposal retained a settings subscription or lost the original background.");
        });

        run("Removing a background feature restores each independent view", () =>
        {
            using var provider = CreateProvider();
            var settings = new BackgroundFeatureSettings { Background = new SolidColorBrush() };
            var container = new ComponentSettings
            {
                Features = [new FeatureInfo { Id = FeatureId, Settings = settings }]
            };
            var original = new SolidColorBrush();
            var first = new Control { Background = original };
            var second = new Border();
            var presenter = new ComponentPresenter();
            presenter.LoadDecorationBorder(second);
            var service = provider.GetRequiredService<IFeatureService>();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            using var firstAttacher = new FeatureAttacher(first, service, registry) { FeatureContainer = container };
            using var secondAttacher = new FeatureAttacher(presenter, service, registry)
            {
                FeatureContainer = container
            };
            firstAttacher.Attach();
            secondAttacher.Attach();
            Check(ReferenceEquals(first.Background, settings.Background) && ReferenceEquals(second.Background, settings.Background),
                "Independent views did not apply the same settings.");
            container.Features.Clear();
            settings.Background = new LinearGradientBrush();
            Check(ReferenceEquals(first.Background, original) && second.Background == null,
                "Feature removal did not release both view bindings.");
        });

        run("Decoration border notifications, delayed realization and replacement preserve binding lifetimes", () =>
        {
            var presenter = new ComponentPresenter();
            var notifications = new List<(string Kind, Border? Border)>();
            presenter.PropertyChanging += (_, args) =>
            {
                if (args.PropertyName == nameof(presenter.Border))
                {
                    notifications.Add(("changing", presenter.Border));
                }
            };
            presenter.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(presenter.Border))
                {
                    notifications.Add(("changed", presenter.Border));
                }
            };
            var settings = new BackgroundFeatureSettings { Background = new SolidColorBrush() };
            using var feature = new BackgroundFeature();
            feature.SetSettings(settings);
            feature.Attach(presenter);
            settings.Background = new LinearGradientBrush();
            var original = new SolidColorBrush();
            var first = new Border { Background = original };
            presenter.LoadDecorationBorder(first);
            Check(notifications.SequenceEqual([("changing", null), ("changed", first)])
                && ReferenceEquals(first.Background, settings.Background),
                "A delayed border did not notify before and after the change or receive the latest background.");
            presenter.LoadDecorationBorder(first);
            Check(notifications.Count == 2, "The same border produced duplicate property notifications.");
            var second = new Border();
            presenter.LoadDecorationBorder(second);
            presenter.UnloadDecorationBorder(first);
            settings.Background = new SolidColorBrush();
            Check(ReferenceEquals(first.Background, original) && ReferenceEquals(second.Background, settings.Background)
                && ReferenceEquals(presenter.Border, second), "A replaced border retained bindings or its stale unload cleared the active border.");
            presenter.UnloadDecorationBorder(second);
            Check(presenter.Border == null && second.Background == null, "Border unload did not restore and release the background.");
            feature.Dispose();
            var afterDisposal = new Border();
            presenter.LoadDecorationBorder(afterDisposal);
            Check(afterDisposal.Background == null, "Disposed features still observed the host border.");
        });

        run("Component content recreation reapplies background to its current decoration border", () =>
        {
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddComponent<TextComponent>();
                services.AddFeature<BackgroundFeature>();
            }).Build();
            var background = new BackgroundFeatureSettings { Background = new LinearGradientBrush() };
            var settings = new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = new TextComponentSettings(),
                Features = [new FeatureInfo { Id = FeatureId, Settings = background }]
            };
            var presenter = new ComponentPresenter { Settings = settings };
            presenter.Load();
            var first = (UserControl)presenter.Content!;
            var firstBorder = new Border();
            presenter.LoadDecorationBorder(firstBorder);
            Check(ReferenceEquals(firstBorder.Background, background.Background) && first.Background == null,
                "The background did not target the decoration border independently of component content.");
            settings.Settings = new TextComponentSettings();
            var second = (UserControl)presenter.Content!;
            var secondBorder = new Border();
            presenter.UnloadDecorationBorder(firstBorder);
            presenter.LoadDecorationBorder(secondBorder);
            Check(!ReferenceEquals(first, second) && firstBorder.Background == null
                && ReferenceEquals(secondBorder.Background, background.Background)
                && ReferenceEquals(BackgroundFeature.ResolveBackgroundTarget(presenter), secondBorder),
                "Content recreation did not release the old border and resolve the new one.");
            background.Background = new SolidColorBrush();
            Check(firstBorder.Background == null && ReferenceEquals(secondBorder.Background, background.Background),
                "Background changes reached an obsolete decoration border.");
            presenter.Unload();
            Check(secondBorder.Background == null && presenter.Border == null && presenter.Content == null,
                "Unload retained a border or background binding.");
            presenter.Load();
            var reloadedBorder = new Border();
            presenter.LoadDecorationBorder(reloadedBorder);
            Check(ReferenceEquals(reloadedBorder.Background, background.Background),
                "Reload did not apply the latest background.");
            presenter.Unload();
        });

        run("Background feature registration creates a settings editor sharing the feature settings", () =>
        {
            using var provider = CreateProvider();
            var service = provider.GetRequiredService<IFeatureService>();
            var info = new FeatureInfo { Id = FeatureId };
            using var feature = (BackgroundFeature)service.CreateFeature(info)!;
            var editor = service.CreateSettingsControl(info);
            Check(editor is BackgroundSettingsControl && ReferenceEquals(editor.DataContext, feature.Settings)
                && feature.Settings.Background == null,
                "Feature registration did not supply the matching editor and shared default settings.");
        });
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddFeature<BackgroundFeature, BackgroundSettingsControl>();
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
