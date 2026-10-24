using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Services.Features.Layout;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

internal static class LayoutFeatureTests
{
    internal static void Run(Action<string, Action> run)
    {
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
            Check(first.Width == 100 && second.Width == 200, "Reattaching retained the old target.");
            var replacement = new LayoutFeatureSettings { IsAutoWidth = false, Width = 300 };
            feature.SetSettings(replacement);
            settings.Width = 400;
            Check(second.Width == 300, "Replacing settings retained the old source.");
            replacement.Width = 500;
            Check(second.Width == 500, "Replacement settings were not observed.");
            feature.Detach();
            feature.Detach();
            replacement.Width = 600;
            Check(feature.Target == null && second.Width == 500, "Detach did not stop synchronization.");
            feature.Attach(first);
            feature.Dispose();
            feature.Dispose();
            replacement.Width = 700;
            Check(feature.Target == null && first.Width == 600, "Dispose did not stop synchronization.");
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
            Check(Position(first).Row == 1 && Position(second).Row == 2, "Reattaching retained the old target.");
            var replacement = new GridLayoutFeatureSettings { Row = 3 };
            feature.SetSettings(replacement);
            settings.Row = 4;
            Check(Position(second).Row == 3, "Replacing settings retained the old source.");
            replacement.Row = 5;
            Check(Position(second).Row == 5, "Replacement settings were not observed.");
            feature.Detach();
            feature.Detach();
            replacement.Row = 6;
            Check(feature.Target == null && Position(second).Row == 5, "Detach did not stop synchronization.");
            feature.Attach(first);
            feature.Dispose();
            feature.Dispose();
            replacement.Row = 7;
            Check(feature.Target == null && Position(first).Row == 6, "Dispose did not stop synchronization.");
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
            Check(Position(target).Row == 2, "Failed attachment retained earlier bindings.");
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
