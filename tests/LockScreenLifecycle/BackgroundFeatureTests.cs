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
using OhMyLockScreen.Shared.Controls.UI;
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
        run("Background target resolution shares the actual painted control or falls back to decorator host", () =>
        {
            var control = new Control();
            var presenter = new ComponentPresenter();
            Check(ReferenceEquals(BackgroundFeature.ResolveBackgroundTarget(control), control)
                && ReferenceEquals(BackgroundFeature.ResolveBackgroundTarget(presenter), presenter),
                "Background target resolution did not return Control or IDecoratorHost fallback before shared border activation.");

            using (var borderLease = presenter.Decorators.AcquireShared(
                DecoratorKeys.Border, static () => new Border(), DecoratorPriorities.Border))
            {
                Check(ReferenceEquals(BackgroundFeature.ResolveBackgroundTarget(presenter), borderLease.Element),
                    "Background target resolution did not resolve the active shared border.");
            }

            Check(ReferenceEquals(BackgroundFeature.ResolveBackgroundTarget(presenter), presenter)
                && BackgroundFeature.ResolveBackgroundTarget(new ContentPresenter { Content = new UserControl() }) == null
                && BackgroundFeature.ResolveBackgroundTarget(new FrameworkElement()) == null
                && BackgroundFeature.ResolveBackgroundTarget(null) == null,
                "Background target resolution did not fall back cleanly or reject unsupported elements.");
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
            var presenter = new ComponentPresenter();
            using var feature = new BackgroundFeature();
            feature.SetSettings(firstSettings);
            feature.Attach(first);
            feature.SetSettings(secondSettings);
            firstSettings.Background = null;
            Check(ReferenceEquals(first.Background, secondBrush), "Old settings still controlled the background.");

            feature.Attach(presenter);
            if (!presenter.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out var second))
            {
                throw new Exception("Reattachment did not acquire a shared border on ComponentPresenter.");
            }

            Check(ReferenceEquals(first.Background, original)
                && ReferenceEquals(second.Background, secondBrush)
                && ReferenceEquals(BackgroundFeature.ResolveBackgroundTarget(presenter), second),
                "Reattachment did not restore the old target and bind the presenter's shared border.");

            feature.Dispose();
            secondSettings.Background = firstBrush;
            Check(second.Background == null
                && !presenter.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out _)
                && ReferenceEquals(first.Background, original),
                "Disposal retained a settings subscription or failed to release the shared border.");
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
            var presenter = new ComponentPresenter();
            var service = provider.GetRequiredService<IFeatureService>();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            using var firstAttacher = new FeatureAttacher(first, service, registry) { FeatureContainer = container };
            using var secondAttacher = new FeatureAttacher(presenter, service, registry)
            {
                FeatureContainer = container
            };
            firstAttacher.Attach();
            secondAttacher.Attach();
            if (!presenter.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out var second))
            {
                throw new Exception("ComponentPresenter did not acquire a shared border.");
            }

            Check(ReferenceEquals(first.Background, settings.Background)
                && ReferenceEquals(second.Background, settings.Background),
                "Independent views did not apply the same settings.");
            container.Features.Clear();
            settings.Background = new LinearGradientBrush();
            Check(ReferenceEquals(first.Background, original)
                && second.Background == null
                && !presenter.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out _),
                "Feature removal did not release both view bindings.");
        });

        run("Shared border lease notifications and coexistence with BorderFeature preserve lifetimes", () =>
        {
            var presenter = new ComponentPresenter();
            var changedCount = 0;
            presenter.Decorators.Changed += (_, _) => changedCount++;

            var settings = new BackgroundFeatureSettings { Background = new SolidColorBrush() };
            using var feature = new BackgroundFeature();
            feature.SetSettings(settings);
            feature.Attach(presenter);
            if (!presenter.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out var border))
            {
                throw new Exception("Attaching BackgroundFeature did not acquire the shared border.");
            }

            Check(changedCount == 1
                && ReferenceEquals(border.Background, settings.Background),
                "Attaching BackgroundFeature did not notify Decorators.Changed and paint the shared border.");

            using (var borderFeature = new BorderFeature())
            {
                borderFeature.SetSettings(new BorderFeatureSettings { BorderThickness = new Thickness(3) });
                borderFeature.Attach(presenter);
                Check(changedCount == 1
                    && presenter.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out var shared)
                    && ReferenceEquals(shared, border)
                    && shared.BorderThickness == new Thickness(3),
                    "BorderFeature did not reuse BackgroundFeature's shared border without extra chain rebuilds.");

                feature.Detach();
                Check(changedCount == 1
                    && presenter.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out _)
                    && border.Background == null
                    && border.BorderThickness == new Thickness(3),
                    "Detaching BackgroundFeature cleared BorderFeature's shared border prematurely.");
            }

            Check(changedCount == 2 && !presenter.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out _),
                "Disposing final lease did not remove the shared border and notify Decorators.Changed.");
        });

        run("Component content recreation reapplies background to its current shared border", () =>
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
            if (!presenter.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out var firstBorder))
            {
                throw new Exception("Loading ComponentPresenter did not acquire the first shared border.");
            }

            Check(ReferenceEquals(firstBorder.Background, background.Background)
                && first.Background == null
                && ReferenceEquals(BackgroundFeature.ResolveBackgroundTarget(presenter), firstBorder),
                "The background did not target the shared border independently of component content.");

            settings.Settings = new TextComponentSettings();
            var second = (UserControl)presenter.Content!;
            if (!presenter.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out var secondBorder))
            {
                throw new Exception("Content recreation did not acquire the new shared border.");
            }

            Check(!ReferenceEquals(first, second)
                && firstBorder.Background == null
                && ReferenceEquals(secondBorder.Background, background.Background)
                && ReferenceEquals(BackgroundFeature.ResolveBackgroundTarget(presenter), secondBorder),
                "Content recreation did not release the old border and bind the new one.");

            background.Background = new SolidColorBrush();
            Check(firstBorder.Background == null && ReferenceEquals(secondBorder.Background, background.Background),
                "Background changes reached an obsolete shared border.");

            presenter.Unload();
            Check(secondBorder.Background == null
                && !presenter.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out _)
                && presenter.Decorators.Elements.Count == 0
                && presenter.Content == null,
                "Unload retained a border or background binding.");

            presenter.Load();
            if (!presenter.Decorators.TryGetShared<Border>(DecoratorKeys.Border, out var reloadedBorder))
            {
                throw new Exception("Reload did not re-acquire the shared border.");
            }

            Check(ReferenceEquals(reloadedBorder.Background, background.Background),
                "Reload did not re-acquire and apply the latest background.");
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
