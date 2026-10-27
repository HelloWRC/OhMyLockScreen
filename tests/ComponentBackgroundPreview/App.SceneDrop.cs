using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Services.Features.Layout;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.Scene;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private async Task VerifyRealizedGridDropAsync(StackPanel root)
    {
        var viewport = new Grid { Width = 600, Height = 400 };
        root.Children.Insert(0, viewport);
        try
        {
            foreach (var id in new[] { TextComponent.Id, ClockComponent.Id })
            {
                var layer = new LayerSettings { Kind = LayoutKind.Grid };
                var scene = new SceneSettings { Layers = [layer] };
                var surface = new ScenePresenter { Scene = scene };
                viewport.Children.Clear();
                viewport.Children.Add(surface);
                viewport.UpdateLayout();
                await Task.Delay(80);
                var control = FindDropVisual<LayoutControl>(surface)!;
                var panel = control.ItemsPanelRoot!;
                var point = new Point(150, 100);
                var local = viewport.TransformToVisual(panel).TransformPoint(point);
                var component = IAppHost.GetRequiredService<ComponentRegistryService>().Find(id)!.CreateSettings();
                var layout = SceneLayoutEditing.GetLayout(component, create: true)!;
                layout.HorizontalAlignment = HorizontalAlignment.Center;
                layout.VerticalAlignment = VerticalAlignment.Center;
                layer.Children.Add(component);
                // Selection creates the inspector before the editor applies the drop position.
                // Segmented initializes its cached selection later, when the inspector loads.
                var inspector = new LayoutSettingsControl { DataContext = layout };
                inspector.SetSettings(layout);
                root.Children.Add(inspector);
                SceneLayoutEditing.ApplyDropPosition(component, panel, local);
                viewport.UpdateLayout();
                await Task.Delay(80);
                viewport.UpdateLayout();
                var presenter = panel.Children.OfType<ComponentPresenter>().Single();
                var actual = presenter.TransformToVisual(viewport).TransformPoint(new Point());
                var origin = panel.TransformToVisual(viewport).TransformPoint(new Point());
                Check(Math.Abs(actual.X - point.X) < 1 && Math.Abs(actual.Y - point.Y) < 1
                    && layout.HorizontalAlignment == HorizontalAlignment.Left && layout.VerticalAlignment == VerticalAlignment.Top,
                    $"Default {id} with inspector in scene Grid drops at ({actual.X}, {actual.Y}), panel origin ({origin.X}, {origin.Y}), alignment ({layout.HorizontalAlignment}, {layout.VerticalAlignment})");
                var alignment = FindDropVisual<CommunityToolkit.WinUI.Controls.Segmented>(inspector)!;
                alignment.SelectedIndex = 2;
                Check(layout.HorizontalAlignment == HorizontalAlignment.Right,
                    $"Loaded {id} inspector still accepts alignment selection changes");
                SceneLayoutEditing.ApplyDropPosition(component, panel, local);
                Check(layout.HorizontalAlignment == HorizontalAlignment.Left && alignment.SelectedIndex == 0,
                    $"Loaded {id} inspector follows source alignment without writing stale selections back");
                for (var placement = 0; placement < 3; placement++)
                {
                    viewport.UpdateLayout();
                    local = placement switch
                    {
                        0 => new Point(24, 32),
                        1 => new Point((panel.ActualWidth - presenter.ActualWidth) / 2,
                            (panel.ActualHeight - presenter.ActualHeight) / 2),
                        _ => new Point(panel.ActualWidth - presenter.ActualWidth - 24,
                            panel.ActualHeight - presenter.ActualHeight - 32)
                    };
                    SceneLayoutEditing.ApplyDropPosition(component, panel, local);
                    viewport.UpdateLayout();
                    var bounds = presenter.TransformToVisual(panel).TransformBounds(
                        new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight));
                    SceneLayoutEditing.AlignDropPosition(component, panel, bounds);
                    viewport.UpdateLayout();
                    actual = presenter.TransformToVisual(panel).TransformPoint(new Point());
                    var expectedHorizontal = placement switch
                    {
                        0 => HorizontalAlignment.Left,
                        1 => HorizontalAlignment.Center,
                        _ => HorizontalAlignment.Right
                    };
                    var expectedVertical = placement switch
                    {
                        0 => VerticalAlignment.Top,
                        1 => VerticalAlignment.Center,
                        _ => VerticalAlignment.Bottom
                    };
                    Check(Math.Abs(actual.X - local.X) < 1 && Math.Abs(actual.Y - local.Y) < 1
                        && layout.HorizontalAlignment == expectedHorizontal && layout.VerticalAlignment == expectedVertical
                        && layout.IsAutoWidth && layout.IsAutoHeight,
                        $"Default {id} drop converts margins to {expectedHorizontal}/{expectedVertical} without moving ({actual.X}, {actual.Y})");
                }
                root.Children.Remove(inspector);
            }
        }
        finally
        {
            root.Children.Remove(viewport);
        }
    }

    private static T? FindDropVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match)
        {
            return match;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            if (FindDropVisual<T>(VisualTreeHelper.GetChild(parent, index)) is { } child)
            {
                return child;
            }
        }

        return null;
    }

    private async Task VerifyDropPositionAsync(StackPanel root)
    {
        var viewport = new Grid { Width = 600, Height = 400 };
        var padding = new Border { Padding = new Thickness(17, 23, 19, 29) };
        viewport.Children.Add(padding);
        root.Children.Insert(0, viewport);
        try
        {
            foreach (Panel panel in new Panel[] { new Grid(), new Canvas() })
            {
                padding.Child = panel;
                var grid = new GridLayoutFeatureSettings { Row = 1, Column = 1, RowSpan = 2, ColumnSpan = 2 };
                if (panel is Grid targetGrid)
                {
                    targetGrid.RowSpacing = 8;
                    targetGrid.ColumnSpacing = 12;
                    foreach (var height in new[] { 60.0, 100, 172 })
                    {
                        targetGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
                    }
                    foreach (var width in new[] { 100.0, 200, 240 })
                    {
                        targetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
                    }
                }

                var component = new ComponentSettings
                {
                    Features = [new FeatureInfo { Id = "omls.features.layout.grid", Settings = grid }]
                };
                var layout = SceneLayoutEditing.GetLayout(component, create: true)!;
                layout.Width = 40;
                layout.Height = 30;
                layout.IsAutoWidth = false;
                layout.IsAutoHeight = false;
                var target = new Border { Background = new SolidColorBrush(Colors.Red) };
                using var layoutFeature = new LayoutFeature();
                layoutFeature.SetSettings(layout);
                layoutFeature.Attach(target);
                using var gridFeature = new GridLayoutFeature();
                gridFeature.SetSettings(grid);
                gridFeature.Attach(target);
                panel.Children.Add(target);
                viewport.UpdateLayout();
                await Task.Delay(50);

                // The point is in viewport coordinates; Padding and nesting must be accounted for exactly once.
                var point = new Point(210, 140);
                var localPoint = viewport.TransformToVisual(panel).TransformPoint(point);
                SceneLayoutEditing.ApplyDropPosition(component, panel, localPoint);
                viewport.UpdateLayout();
                var actual = target.TransformToVisual(viewport).TransformPoint(new Point());
                Check(Math.Abs(actual.X - point.X) < 1 && Math.Abs(actual.Y - point.Y) < 1,
                    $"{panel.GetType().Name} drop uses native top-left coordinates through nested padding ({actual.X}, {actual.Y})");

                // Keep the assigned cell even when the drop is above and to its left.
                point = new Point(40, 45);
                localPoint = viewport.TransformToVisual(panel).TransformPoint(point);
                SceneLayoutEditing.ApplyDropPosition(component, panel, localPoint);
                viewport.UpdateLayout();
                actual = target.TransformToVisual(viewport).TransformPoint(new Point());
                Check(Math.Abs(actual.X - point.X) < 1 && Math.Abs(actual.Y - point.Y) < 1
                    && grid is { Row: 1, Column: 1, RowSpan: 2, ColumnSpan: 2 }
                    && target.ActualWidth == 40 && target.ActualHeight == 30,
                    $"{panel.GetType().Name} drop preserves cell and size with negative cell-relative margins ({actual.X}, {actual.Y})");

                var area = SceneLayoutEditing.GetLayoutArea(panel, grid.Row, grid.Column, grid.RowSpan, grid.ColumnSpan);
                localPoint = new Point(area.X + area.Width - target.ActualWidth - 24,
                    area.Y + area.Height - target.ActualHeight - 32);
                SceneLayoutEditing.ApplyDropPosition(component, panel, localPoint);
                viewport.UpdateLayout();
                var bounds = target.TransformToVisual(panel).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                SceneLayoutEditing.AlignDropPosition(component, panel, bounds);
                viewport.UpdateLayout();
                actual = target.TransformToVisual(panel).TransformPoint(new Point());
                Check(Math.Abs(actual.X - localPoint.X) < 1 && Math.Abs(actual.Y - localPoint.Y) < 1
                    && layout.HorizontalAlignment == (panel is Grid ? HorizontalAlignment.Right : HorizontalAlignment.Left)
                    && layout.VerticalAlignment == (panel is Grid ? VerticalAlignment.Bottom : VerticalAlignment.Top)
                    && target.ActualWidth == 40 && target.ActualHeight == 30
                    && grid is { Row: 1, Column: 1, RowSpan: 2, ColumnSpan: 2 },
                    $"{panel.GetType().Name} fixed-size drop preserves bounds and cells when converting edge margins");
                panel.Children.Clear();
            }
        }
        finally
        {
            root.Children.Remove(viewport);
        }
    }
}
