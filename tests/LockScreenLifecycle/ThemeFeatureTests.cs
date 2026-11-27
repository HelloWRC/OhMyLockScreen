using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Services.Features.Appearance;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

internal static class ThemeFeatureTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Theme overrides only its current target and restores local, bound and unset values", () =>
        {
            var property = FrameworkElement.RequestedThemeProperty;
            var grid = new Grid { RequestedTheme = ElementTheme.Dark };
            var child = new TextBlock();
            grid.Children.Add(child);
            var source = new ThemeFeatureSettings { RequestedTheme = ElementTheme.Light };
            var binding = new Binding { Source = source, Path = new PropertyPath(nameof(source.RequestedTheme)) };
            child.SetBinding(property, binding);

            var settings = new ThemeFeatureSettings();
            using var feature = new ThemeFeature();
            feature.SetSettings(settings);
            feature.Attach(grid);
            Check(grid.RequestedTheme == ElementTheme.Default);
            settings.RequestedTheme = ElementTheme.Light;
            Check(grid.RequestedTheme == ElementTheme.Light && child.GetBindingExpression(property)?.ParentBinding == binding);

            feature.Attach(child);
            settings.RequestedTheme = ElementTheme.Dark;
            Check(grid.RequestedTheme == ElementTheme.Dark && child.RequestedTheme == ElementTheme.Dark);
            var replacement = new ThemeFeatureSettings { RequestedTheme = ElementTheme.Light };
            feature.SetSettings(replacement);
            settings.RequestedTheme = ElementTheme.Default;
            Check(child.RequestedTheme == ElementTheme.Light);

            var unthemed = new Grid();
            feature.Attach(unthemed);
            Check(child.GetBindingExpression(property)?.ParentBinding == binding);
            feature.Detach();
            replacement.RequestedTheme = ElementTheme.Dark;
            Check(unthemed.ReadLocalValue(property) == DependencyProperty.UnsetValue);
        });
    }

    private static void Check(bool condition, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? message = null)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
