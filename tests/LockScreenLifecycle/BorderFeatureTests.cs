using Microsoft.Extensions.DependencyInjection;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using OhMyLockScreen.ViewModels;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

internal static class BorderFeatureTests
{
    private const string FeatureId = "omls.features.appearance.border";

    internal static void Run(Action<string, Action> run)
    {
        run("Border defaults copy the current theme foreground into an independent one-unit outline", () =>
        {
            var app = new Application();
            var themeBrush = new SolidColorBrush(Color.FromArgb(255, 12, 34, 56));
            app.Resources["SystemControlForegroundBaseHighBrush"] = themeBrush;
            var settings = new BorderFeatureSettings();
            Check(settings.BorderBrush is SolidColorBrush brush && brush.Color == themeBrush.Color
                && !ReferenceEquals(brush, themeBrush) && settings.BorderThickness == new Thickness(1)
                && settings.CornerRadius == default && settings.Padding == default,
                "Border defaults did not copy the theme color or use the expected geometry.");
            themeBrush.Color = Color.FromArgb(255, 65, 43, 21);
            Check(((SolidColorBrush)settings.BorderBrush!).Color == Color.FromArgb(255, 12, 34, 56),
                "A theme change mutated an existing feature's brush.");
            var another = new BorderFeatureSettings();
            Check(!ReferenceEquals(settings.BorderBrush, another.BorderBrush)
                && ((SolidColorBrush)another.BorderBrush!).Color == themeBrush.Color,
                "New features did not capture independent current theme colors.");
        });

        run("Border properties synchronize immediately and detach restores all original values", () =>
        {
            var border = OriginalBorder();
            var original = Snapshot(border);
            var presenter = new ComponentPresenter();
            presenter.LoadDecorationBorder(border);
            var settings = new BorderFeatureSettings();
            using var feature = new BorderFeature();
            feature.SetSettings(settings);
            feature.Attach(presenter);
            Check(Snapshot(border) == Snapshot(settings), "Initial border values were not applied.");
            settings.BorderBrush = new LinearGradientBrush();
            settings.BorderThickness = new Thickness(1.5, 2.5, 3.5, 4.5);
            settings.CornerRadius = new CornerRadius(5.5, 6.5, 7.5, 8.5);
            settings.Padding = new Thickness(9.5, 10.5, 11.5, 12.5);
            Check(Snapshot(border) == Snapshot(settings), "Border changes did not reach the decoration border.");
            settings.BorderBrush = null;
            Check(border.BorderBrush == null, "Clearing the brush did not clear the outline.");
            feature.Detach();
            feature.Detach();
            settings.BorderThickness = new Thickness(100);
            Check(Snapshot(border) == original && feature.Target == null,
                "Detach retained a binding or failed to restore the border.");
        });

        run("Border delayed realization, replacement and unload restore obsolete instances", () =>
        {
            var presenter = new ComponentPresenter();
            var settings = new BorderFeatureSettings();
            using var feature = new BorderFeature();
            feature.SetSettings(settings);
            feature.Attach(presenter);
            settings.Padding = new Thickness(7, 8, 9, 10);
            var first = OriginalBorder();
            var original = Snapshot(first);
            presenter.LoadDecorationBorder(first);
            Check(Snapshot(first) == Snapshot(settings), "A delayed border missed the latest settings.");
            presenter.LoadDecorationBorder(first);
            var second = new Border();
            presenter.LoadDecorationBorder(second);
            presenter.UnloadDecorationBorder(first);
            settings.CornerRadius = new CornerRadius(12, 13, 14, 15);
            Check(Snapshot(first) == original && Snapshot(second) == Snapshot(settings),
                "Border replacement or stale unload retained an obsolete binding.");
            presenter.UnloadDecorationBorder(second);
            Check(Snapshot(second) == default && presenter.Border == null,
                "Unloading the border failed to restore it.");
            presenter.LoadDecorationBorder(second);
            Check(Snapshot(second) == Snapshot(settings), "Reload did not apply current settings.");
            feature.Dispose();
            feature.Dispose();
            presenter.LoadDecorationBorder(first);
            settings.Padding = default;
            Check(Snapshot(first) == original && Snapshot(second) == default,
                "Disposal retained border notifications or settings bindings.");
        });

        run("Border settings replacement and reattachment release obsolete settings and hosts", () =>
        {
            var first = new ComponentPresenter();
            var firstBorder = OriginalBorder();
            var original = Snapshot(firstBorder);
            first.LoadDecorationBorder(firstBorder);
            var second = new ComponentPresenter();
            var secondBorder = new Border();
            second.LoadDecorationBorder(secondBorder);
            var settings = new BorderFeatureSettings();
            var replacement = new BorderFeatureSettings { Padding = new Thickness(24) };
            using var feature = new BorderFeature();
            feature.SetSettings(settings);
            feature.Attach(first);
            feature.SetSettings(replacement);
            settings.BorderThickness = new Thickness(42);
            Check(Snapshot(firstBorder) == Snapshot(replacement), "Old settings still controlled the border.");
            feature.Attach(second);
            feature.Attach(second);
            replacement.CornerRadius = new CornerRadius(8);
            first.LoadDecorationBorder(new Border());
            Check(Snapshot(firstBorder) == original && Snapshot(secondBorder) == Snapshot(replacement)
                && first.Border!.BorderBrush == null,
                "Reattachment retained old settings, host notifications or border values.");
        });

        run("Removing a border feature restores independent views sharing one settings model", () =>
        {
            using var provider = CreateProvider();
            var settings = new BorderFeatureSettings { Padding = new Thickness(12) };
            var container = new ComponentSettings
            {
                Features = [new FeatureInfo { Id = FeatureId, Settings = settings }]
            };
            var first = new ComponentPresenter();
            var firstBorder = OriginalBorder();
            var original = Snapshot(firstBorder);
            first.LoadDecorationBorder(firstBorder);
            var second = new ComponentPresenter();
            var secondBorder = new Border();
            second.LoadDecorationBorder(secondBorder);
            var service = provider.GetRequiredService<IFeatureService>();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            using var firstAttacher = new FeatureAttacher(first, service, registry) { FeatureContainer = container };
            using var secondAttacher = new FeatureAttacher(second, service, registry) { FeatureContainer = container };
            firstAttacher.Attach();
            secondAttacher.Attach();
            settings.CornerRadius = new CornerRadius(14);
            Check(Snapshot(firstBorder) == Snapshot(settings) && Snapshot(secondBorder) == Snapshot(settings),
                "Independent views did not receive shared border changes.");
            container.Features.Clear();
            settings.Padding = default;
            Check(Snapshot(firstBorder) == original && Snapshot(secondBorder) == default,
                "Removing the feature failed to restore both independent views.");
        });

        run("Failed border attachment releases bindings established before a native geometry error", () =>
        {
            var presenter = new ComponentPresenter();
            var border = OriginalBorder();
            var original = Snapshot(border);
            presenter.LoadDecorationBorder(border);
            var settings = new BorderFeatureSettings { CornerRadius = new CornerRadius(-1) };
            using var feature = new BorderFeature();
            feature.SetSettings(settings);
            try
            {
                feature.Attach(presenter);
                throw new Exception("Invalid corner radii were accepted.");
            }
            catch (ArgumentException)
            {
                Check(feature.Target == null && Snapshot(border) == original,
                    "Failed attachment retained a target or partially applied border properties.");
            }

            settings.BorderBrush = null;
            settings.BorderThickness = new Thickness(20);
            Check(Snapshot(border) == original, "A failed attachment retained settings subscriptions.");
            settings.CornerRadius = new CornerRadius(6);
            feature.Attach(presenter);
            Check(Snapshot(border) == Snapshot(settings), "The feature could not recover from failed attachment.");
        });

        run("Border registration only accepts IBorderable hosts and supplies the matching editor", () =>
        {
            using var provider = CreateProvider();
            var registration = provider.GetRequiredService<FeatureRegistryService>().Find(FeatureId)!;
            Check(registration.TargetType == typeof(IBorderable)
                && registration.TargetType.IsAssignableFrom(typeof(ComponentPresenter))
                && !registration.TargetType.IsAssignableFrom(typeof(Control))
                && !registration.TargetType.IsAssignableFrom(typeof(Border)),
                "Border target filtering accepted an unsupported host.");
            var service = provider.GetRequiredService<IFeatureService>();
            var info = new FeatureInfo { Id = FeatureId };
            using var feature = (BorderFeature)service.CreateFeature(info)!;
            var editor = service.CreateSettingsControl(info);
            Check(editor is BorderSettingsControl && ReferenceEquals(editor.DataContext, feature.Settings),
                "Registration did not create a matching editor sharing the settings.");
        });

        run("Border numeric editing preserves other components, rejects invalid input and releases subscriptions", () =>
        {
            var settings = new BorderFeatureSettings
            {
                BorderThickness = new Thickness(1, 2, 3, 4),
                CornerRadius = new CornerRadius(5, 6, 7, 8),
                Padding = new Thickness(9, 10, 11, 12)
            };
            using var values = new BorderSettingsViewModel(settings);
            values.Attach();
            values.Attach();
            values.ThicknessLeft = 1.25;
            values.ThicknessTop = 2.25;
            values.ThicknessRight = 3.25;
            values.ThicknessBottom = 4.25;
            values.RadiusTopLeft = 5.25;
            values.RadiusTopRight = 6.25;
            values.RadiusBottomRight = 7.25;
            values.RadiusBottomLeft = 8.25;
            values.PaddingLeft = 9.25;
            values.PaddingTop = 10.25;
            values.PaddingRight = 11.25;
            values.PaddingBottom = 12.25;
            Check(settings.BorderThickness == new Thickness(1.25, 2.25, 3.25, 4.25)
                && settings.CornerRadius == new CornerRadius(5.25, 6.25, 7.25, 8.25)
                && settings.Padding == new Thickness(9.25, 10.25, 11.25, 12.25),
                "Editing a component overwrote a different side or corner.");
            var expected = Snapshot(settings);
            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 100001 })
            {
                values.ThicknessLeft = invalid;
                values.RadiusBottomLeft = invalid;
                values.PaddingTop = invalid;
            }

            Check(Snapshot(settings) == expected, "Invalid numeric input reached the settings model.");
            var changed = new List<string?>();
            values.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
            settings.CornerRadius = new CornerRadius(10, 20, 30, 40);
            Check(changed.SequenceEqual(new[] { nameof(values.RadiusTopLeft), nameof(values.RadiusTopRight),
                    nameof(values.RadiusBottomRight), nameof(values.RadiusBottomLeft) })
                && values.RadiusBottomLeft == 40, "External settings changes did not refresh the numeric fields.");
            values.Dispose();
            values.Dispose();
            settings.CornerRadius = default;
            Check(changed.Count == 4, "An unloaded editor retained its settings subscription.");
            values.Attach();
            changed.Clear();
            settings.Padding = new Thickness(100000, 0, 1, 2);
            Check(changed.Count == 4 && values.PaddingLeft == 100000,
                "Reloading the editor failed to resume settings notifications.");
        });
    }

    private static Border OriginalBorder() => new()
    {
        BorderBrush = new SolidColorBrush(Color.FromArgb(255, 3, 2, 1)),
        BorderThickness = new Thickness(2, 3, 4, 5),
        CornerRadius = new CornerRadius(6, 7, 8, 9),
        Padding = new Thickness(10, 11, 12, 13)
    };

    private static (Brush?, Thickness, CornerRadius, Thickness) Snapshot(Border border) =>
        (border.BorderBrush, border.BorderThickness, border.CornerRadius, border.Padding);

    private static (Brush?, Thickness, CornerRadius, Thickness) Snapshot(BorderFeatureSettings settings) =>
        (settings.BorderBrush, settings.BorderThickness, settings.CornerRadius, settings.Padding);

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddFeature<BorderFeature, BorderSettingsControl>();
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
