using Microsoft.Extensions.DependencyInjection;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

internal static class LayoutFeatureTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Removing layout features from the collection restores the target", () =>
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IFeatureService, FeatureService>();
            services.AddFeature<LayoutFeature>();
            services.AddFeature<GridLayoutFeature>();
            using var provider = services.BuildServiceProvider();
            var container = new ComponentSettings();
            var target = new Control();
            using var attacher = new FeatureAttacher(target,
                provider.GetRequiredService<IFeatureService>(),
                provider.GetRequiredService<FeatureRegistryService>())
            {
                FeatureContainer = container
            };
            attacher.Attach();
            var layout = new FeatureInfo
            {
                Id = "omls.features.layout.layout",
                Settings = new LayoutFeatureSettings
                {
                    Width = 120,
                    Height = 60,
                    IsAutoWidth = false,
                    IsAutoHeight = false,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(1, 2, 3, 4)
                }
            };
            var grid = new FeatureInfo
            {
                Id = "omls.features.layout.grid",
                Settings = new GridLayoutFeatureSettings { Row = 2, Column = 3, RowSpan = 4, ColumnSpan = 5 }
            };
            container.Features.Add(layout);
            container.Features.Add(grid);
            Check(target.Width == 120 && target.Height == 60 && Position(target) == (2, 3, 4, 5),
                "Adding features did not apply the layout.");
            container.Features.Remove(layout);
            Check(double.IsNaN(target.Width) && double.IsNaN(target.Height)
                && target.HorizontalAlignment == HorizontalAlignment.Stretch
                && target.VerticalAlignment == VerticalAlignment.Stretch && target.Margin == default
                && Position(target) == (2, 3, 4, 5),
                "Removing the layout feature did not restore defaults or affected the grid feature.");
            container.Features.Remove(grid);
            Check(Position(target) == (0, 0, 1, 1), "Removing the grid feature did not restore defaults.");
        });

        run("Layout features synchronize dimensions, alignment and margins", () =>
        {
            var settings = new LayoutFeatureSettings
            {
                Width = 120,
                Height = 60,
                IsAutoWidth = false,
                IsAutoHeight = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(1, 2, 3, 4)
            };
            using var feature = new LayoutFeature();
            feature.SetSettings(settings);
            var target = new Control();
            feature.Attach(target);
            Check(ReferenceEquals(feature.Target, target) && target.Width == 120 && target.Height == 60
                && target.HorizontalAlignment == HorizontalAlignment.Center
                && target.VerticalAlignment == VerticalAlignment.Bottom && target.Margin == settings.Margin,
                "Initial layout values were not applied.");

            settings.Width = 240;
            settings.Height = 100;
            settings.HorizontalAlignment = HorizontalAlignment.Right;
            settings.VerticalAlignment = VerticalAlignment.Center;
            settings.Margin = new Thickness(4, 3, 2, 1);
            Check(target.Width == 240 && target.Height == 100
                && target.HorizontalAlignment == HorizontalAlignment.Right
                && target.VerticalAlignment == VerticalAlignment.Center && target.Margin == settings.Margin,
                "Updated layout values were not applied.");
        });

        run("Automatic dimensions follow toggles and retain the latest explicit sizes", () =>
        {
            var settings = new LayoutFeatureSettings();
            using var feature = new LayoutFeature();
            feature.SetSettings(settings);
            var target = new Control { Width = 10, Height = 20 };
            feature.Attach(target);
            Check(double.IsNaN(target.Width) && double.IsNaN(target.Height), "Auto sizes were not applied initially.");
            settings.Width = 200;
            settings.Height = 90;
            Check(double.IsNaN(target.Width) && double.IsNaN(target.Height), "Explicit values overrode auto sizes.");
            settings.IsAutoWidth = false;
            Check(target.Width == 200 && double.IsNaN(target.Height), "Width mode affected height.");
            settings.IsAutoHeight = false;
            Check(target.Height == 90, "Height mode did not restore the latest size.");
            settings.IsAutoWidth = true;
            settings.IsAutoHeight = true;
            Check(double.IsNaN(target.Width) && double.IsNaN(target.Height), "Auto mode was not restored.");
        });

        run("Detaching layout features restores all original values", () =>
        {
            var settings = new LayoutFeatureSettings
            {
                Width = 120,
                Height = 60,
                IsAutoWidth = false,
                IsAutoHeight = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(1, 2, 3, 4)
            };
            using var feature = new LayoutFeature();
            feature.SetSettings(settings);
            var target = new Control
            {
                Width = 30,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4, 3, 2, 1)
            };
            feature.Attach(target);
            settings.IsAutoWidth = true;
            settings.IsAutoHeight = true;
            feature.Detach();
            settings.HorizontalAlignment = HorizontalAlignment.Left;
            settings.VerticalAlignment = VerticalAlignment.Center;
            settings.Margin = new Thickness(9, 9, 9, 9);
            Check(target.Width == 30 && target.Height == 40
                && target.HorizontalAlignment == HorizontalAlignment.Right
                && target.VerticalAlignment == VerticalAlignment.Top && target.Margin == new Thickness(4, 3, 2, 1),
                "Detaching did not restore the layout values from before attachment.");
        });

        run("Detaching grid features restores all original attached values", () =>
        {
            var settings = new GridLayoutFeatureSettings { Row = 1, Column = 2, RowSpan = 3, ColumnSpan = 4 };
            using var feature = new GridLayoutFeature();
            feature.SetSettings(settings);
            var target = new Control();
            target.SetValue(Grid.RowProperty, 4);
            target.SetValue(Grid.ColumnProperty, 3);
            target.SetValue(Grid.RowSpanProperty, 2);
            target.SetValue(Grid.ColumnSpanProperty, 1);
            feature.Attach(target);
            Check(Position(target) == (1, 2, 3, 4), "Grid values were not applied.");
            feature.Detach();
            settings.Row = 5;
            settings.Column = 6;
            settings.RowSpan = 7;
            settings.ColumnSpan = 8;
            Check(Position(target) == (4, 3, 2, 1), "Detaching did not restore the original grid values.");
        });

        run("Grid features synchronize attached properties and use valid default spans", () =>
        {
            var settings = new GridLayoutFeatureSettings();
            using var feature = new GridLayoutFeature();
            feature.SetSettings(settings);
            var target = new Control();
            feature.Attach(target);
            Check(Position(target) == (0, 0, 1, 1), "Default grid settings were invalid.");
            settings.Row = 2;
            settings.Column = 3;
            settings.RowSpan = 4;
            settings.ColumnSpan = 5;
            Check(Position(target) == (2, 3, 4, 5), "Grid settings did not update attached properties.");
            feature.Detach();
            feature.Attach(target);
            Check(Position(target) == (2, 3, 4, 5), "Initial non-default grid settings were not applied.");
        });

        run("Layout bindings release old targets and settings on reattach and disposal", () =>
        {
            var settings = new LayoutFeatureSettings { IsAutoWidth = false, Width = 100 };
            using var feature = new LayoutFeature();
            feature.SetSettings(settings);
            var first = new Control();
            var second = new Control();
            feature.Attach(first);
            feature.Attach(second);
            feature.Attach(second);
            settings.Width = 200;
            Check(double.IsNaN(first.Width) && second.Width == 200, "Reattaching did not restore the old target.");
            var replacement = new LayoutFeatureSettings { IsAutoWidth = false, Width = 300 };
            feature.SetSettings(replacement);
            settings.Width = 400;
            Check(second.Width == 300, "Replacing settings retained the old source.");
            replacement.Width = 500;
            Check(second.Width == 500, "Replacement settings were not observed.");
            feature.Detach();
            feature.Detach();
            replacement.Width = 600;
            Check(feature.Target == null && double.IsNaN(second.Width), "Detach did not restore the original size.");
            feature.Attach(first);
            feature.Dispose();
            feature.Dispose();
            replacement.Width = 700;
            Check(feature.Target == null && double.IsNaN(first.Width), "Dispose did not restore the original size.");
        });

        run("Grid bindings release old targets and settings on reattach and disposal", () =>
        {
            var settings = new GridLayoutFeatureSettings { Row = 1 };
            using var feature = new GridLayoutFeature();
            feature.SetSettings(settings);
            var first = new Control();
            var second = new Control();
            feature.Attach(first);
            feature.Attach(second);
            feature.Attach(second);
            settings.Row = 2;
            Check(Position(first).Row == 0 && Position(second).Row == 2, "Reattaching did not restore the old target.");
            var replacement = new GridLayoutFeatureSettings { Row = 3 };
            feature.SetSettings(replacement);
            settings.Row = 4;
            Check(Position(second).Row == 3, "Replacing settings retained the old source.");
            replacement.Row = 5;
            Check(Position(second).Row == 5, "Replacement settings were not observed.");
            feature.Detach();
            feature.Detach();
            replacement.Row = 6;
            Check(feature.Target == null && Position(second).Row == 0, "Detach did not restore the original row.");
            feature.Attach(first);
            feature.Dispose();
            feature.Dispose();
            replacement.Row = 7;
            Check(feature.Target == null && Position(first).Row == 0, "Dispose did not restore the original row.");
        });

        run("Failed grid attachment releases bindings established before the failure", () =>
        {
            var settings = new GridLayoutFeatureSettings { Row = 2, RowSpan = 0 };
            using var feature = new GridLayoutFeature();
            feature.SetSettings(settings);
            var target = new Control();
            try
            {
                feature.Attach(target);
                throw new Exception("Invalid span was accepted.");
            }
            catch (ArgumentException)
            {
                Check(feature.Target == null, "Failed attachment retained its target.");
            }

            settings.Row = 3;
            Check(Position(target) == (0, 0, 1, 1), "Failed attachment did not restore the original grid values.");
            settings.RowSpan = 1;
            feature.Attach(target);
            Check(Position(target) == (3, 0, 1, 1), "The feature could not recover from failed attachment.");
        });
    }

    private static (int Row, int Column, int RowSpan, int ColumnSpan) Position(Control target)
        => ((int)target.GetValue(Grid.RowProperty)!, (int)target.GetValue(Grid.ColumnProperty)!,
            (int)target.GetValue(Grid.RowSpanProperty)!, (int)target.GetValue(Grid.ColumnSpanProperty)!);

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
