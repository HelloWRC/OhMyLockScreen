using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CommunityToolkit.WinUI.Controls;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

internal static class LayoutTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Layout is an ItemsControl with a native Canvas default", () =>
        {
            var control = new LayoutControl();
            Check(control.Settings == null && control.ItemsPanelRoot is Canvas, "Missing Canvas default.");
            Check(control is ItemsControl, "Layout does not expose the standard ItemsControl contract.");
            control.Settings = new LayerSettings { Kind = (LayoutKind)100 };
            Check(control.ItemsPanelRoot is Canvas, "Unknown layout did not fall back to Canvas.");
        });

        run("Switching native ItemsPanel templates leaves ItemsSource and ItemTemplate with ItemsControl", () =>
        {
            var settings = new LayerSettings();
            var source = new ObservableCollection<ComponentSettings> { new(), new() };
            var template = new DataTemplate();
            var control = new LayoutControl { Settings = settings, ItemsSource = source, ItemTemplate = template };
            control.Load();
            foreach (var kind in new[] { LayoutKind.StackPanel, LayoutKind.WrapPanel, LayoutKind.Grid, LayoutKind.Canvas })
            {
                settings.Kind = kind;
                Check(control.ItemsPanelRoot?.GetType().Name == kind.ToString(), "Incorrect native panel was selected.");
                Check(ReferenceEquals(control.ItemsSource, source) && ReferenceEquals(control.ItemTemplate, template),
                    "Layout switching overwrote the standard item source or template.");
            }

            control.Settings = null;
            Check(control.ItemsPanelRoot is Canvas && ReferenceEquals(control.ItemsSource, source),
                "Clearing settings lost the source or Canvas fallback.");
            control.Unload();
        });

        run("Deferred and recreated items panels receive current settings without repeated layout work", () =>
        {
            var settings = new LayerSettings { Kind = LayoutKind.Grid, Rows = [new GridElement()] };
            var control = new LayoutControl { DeferPanelCreation = true, Settings = settings };
            control.Load();
            Check(control.ItemsPanelRoot is Canvas, "Test did not defer panel realization.");
            settings.Spacing = 15;
            settings.Rows[0].Value = 4;
            control.RealizeItemsPanel();
            var grid = (Grid)control.ItemsPanelRoot!;
            Check(grid.RowSpacing == 15 && grid.RowDefinitions[0].Height.Value == 4,
                "Deferred panel missed current settings.");
            var definition = grid.RowDefinitions[0];
            control.RaiseLayoutUpdated();
            Check(ReferenceEquals(definition, grid.RowDefinitions[0]), "Every layout pass rebuilt the grid definitions.");
            control.RealizeItemsPanel();
            var nextGrid = (Grid)control.ItemsPanelRoot!;
            Check(!ReferenceEquals(grid, nextGrid) && nextGrid.RowDefinitions[0].Height.Value == 4,
                "A recreated panel did not receive layout settings.");
            control.Unload();
            control.RealizeItemsPanel();
            Check(((Grid)control.ItemsPanelRoot!).RowDefinitions.Count == 0,
                "Unloaded control retained its layout event handler.");
            control.Load();
            Check(((Grid)control.ItemsPanelRoot!).RowDefinitions[0].Height.Value == 4,
                "Reload did not configure the current panel.");
            control.Unload();
        });

        run("Stack and wrap settings update in place and settings replacement detaches the old model", () =>
        {
            var settings = new LayerSettings { Kind = LayoutKind.StackPanel, Spacing = 12, Orientation = Orientation.Horizontal };
            var control = new LayoutControl { Settings = settings };
            var stack = (StackPanel)control.ItemsPanelRoot!;
            Check(stack.Spacing == 12 && stack.Orientation == Orientation.Horizontal, "Initial assignment was not applied before loading.");
            control.Load();
            settings.Spacing = 21;
            settings.Orientation = Orientation.Vertical;
            Check(ReferenceEquals(stack, control.ItemsPanelRoot) && stack.Spacing == 21 && stack.Orientation == Orientation.Vertical,
                "Stack parameters did not update in place.");
            var next = new LayerSettings { Kind = LayoutKind.WrapPanel, Spacing = 5, Orientation = Orientation.Horizontal };
            control.Settings = next;
            var wrap = (WrapPanel)control.ItemsPanelRoot!;
            next.Spacing = 9;
            next.Orientation = Orientation.Vertical;
            Check(ReferenceEquals(wrap, control.ItemsPanelRoot) && wrap.HorizontalSpacing == 9 && wrap.VerticalSpacing == 9
                && wrap.Orientation == Orientation.Vertical, "Wrap parameters did not update in place.");
            settings.Kind = LayoutKind.Grid;
            Check(ReferenceEquals(wrap, control.ItemsPanelRoot), "Detached settings changed the layout.");
            control.Unload();
        });

        run("Grid definitions map star, fixed, auto, bounds and explicit zero values", () =>
        {
            var settings = new LayerSettings
            {
                Kind = LayoutKind.Grid,
                Spacing = 7,
                Rows =
                [
                    new GridElement { Value = 2, MinValue = 10, MaxValue = 100 },
                    new GridElement { Mode = GridElementMode.Fixed, Value = 40 },
                    new GridElement { Mode = GridElementMode.Auto }
                ],
                Columns = [new GridElement { Value = 0, MaxValue = 0 }]
            };
            var control = new LayoutControl { Settings = settings };
            var grid = (Grid)control.ItemsPanelRoot!;
            Check(grid.RowSpacing == 7 && grid.ColumnSpacing == 7, "Grid spacing is wrong.");
            Check(grid.RowDefinitions[0].Height == new GridLength(2, GridUnitType.Star)
                && grid.RowDefinitions[0].MinHeight == 10 && grid.RowDefinitions[0].MaxHeight == 100,
                "Star row or bounds are wrong.");
            Check(grid.RowDefinitions[1].Height == new GridLength(40, GridUnitType.Pixel)
                && double.IsPositiveInfinity(grid.RowDefinitions[1].MaxHeight)
                && grid.RowDefinitions[2].Height == GridLength.Auto, "Fixed or auto row is wrong.");
            Check(grid.ColumnDefinitions[0].Width == new GridLength(0, GridUnitType.Star)
                && grid.ColumnDefinitions[0].MaxWidth == 0, "Explicit zero was replaced by a default.");
        });

        run("Grid tracks collection mutations, replaced collections and shared definition objects", () =>
        {
            var shared = new GridElement { Value = 3 };
            var settings = new LayerSettings { Kind = LayoutKind.Grid, Rows = [shared], Columns = [shared, shared] };
            var control = new LayoutControl { Settings = settings };
            control.Load();
            var grid = (Grid)control.ItemsPanelRoot!;
            shared.Value = 4;
            shared.MinValue = 2;
            shared.MaxValue = 200;
            Check(grid.RowDefinitions[0].Height.Value == 4 && grid.ColumnDefinitions.All(column => column.Width.Value == 4
                && column.MinWidth == 2 && column.MaxWidth == 200), "Shared definition changes did not reach every location.");
            var fixedRow = new GridElement { Mode = GridElementMode.Fixed, Value = 20 };
            settings.Rows.Add(fixedRow);
            settings.Rows.Move(1, 0);
            Check(grid.RowDefinitions[0].Height == new GridLength(20, GridUnitType.Pixel), "Row add or move failed.");
            settings.Rows[0] = new GridElement { Mode = GridElementMode.Auto };
            var firstDefinition = grid.RowDefinitions[0];
            fixedRow.Value = 30;
            Check(ReferenceEquals(firstDefinition, grid.RowDefinitions[0]), "Removed row retained a subscription.");
            settings.Rows.RemoveAt(1);
            Check(grid.RowDefinitions.Count == 1, "Row removal failed.");
            var oldColumns = settings.Columns;
            settings.Columns = [new GridElement { Mode = GridElementMode.Fixed, Value = 60 }];
            var columnDefinition = grid.ColumnDefinitions[0];
            oldColumns.Clear();
            shared.Value = 50;
            Check(ReferenceEquals(columnDefinition, grid.ColumnDefinitions[0]), "Old collection or element retained a subscription.");
            settings.Rows.Clear();
            settings.Columns.Clear();
            Check(grid.RowDefinitions.Count == 0 && grid.ColumnDefinitions.Count == 0, "Empty grid did not retain implicit definitions.");
            settings.Rows = settings.Columns;
            settings.Rows.Add(shared);
            shared.Mode = GridElementMode.Fixed;
            Check(grid.RowDefinitions[0].Height == new GridLength(50, GridUnitType.Pixel)
                && grid.ColumnDefinitions[0].Width == grid.RowDefinitions[0].Height, "Shared collection was not handled.");
            control.Unload();
        });

        run("Loading is idempotent and unloading detaches settings, collections and elements", () =>
        {
            var settings = new NotifyingLayoutSettings { Kind = LayoutKind.Grid, Rows = [new GridElement()] };
            var control = new LayoutControl { Settings = settings };
            Check(settings.Subscribers == 0, "Unloaded control subscribed to settings.");
            control.Load();
            control.Load();
            Check(settings.Subscribers == 1, "Repeated load duplicated subscriptions.");
            var grid = (Grid)control.ItemsPanelRoot!;
            var definition = grid.RowDefinitions[0];
            control.Unload();
            Check(settings.Subscribers == 0, "Unload retained the settings subscription.");
            settings.Rows[0].Value = 5;
            settings.Rows.Add(new GridElement());
            settings.Spacing = 19;
            settings.NotifyAll();
            Check(ReferenceEquals(definition, grid.RowDefinitions[0]) && grid.RowDefinitions.Count == 1 && grid.RowSpacing == 0,
                "Unloaded control responded to settings or collection events.");
            control.Load();
            Check(grid.RowDefinitions.Count == 2 && grid.RowDefinitions[0].Height.Value == 5 && grid.RowSpacing == 19,
                "Reload did not apply current settings.");
            settings.Spacing = 23;
            settings.NotifyAll();
            Check(grid.RowSpacing == 23 && settings.Subscribers == 1, "All-properties notification failed.");
            control.Unload();
        });

        run("Cross-view notifications use the owning dispatcher and stale callbacks are ignored", () =>
        {
            var owner = Window.Current;
            var settings = new LayerSettings { Kind = LayoutKind.Grid, Rows = [new GridElement()] };
            var control = new LayoutControl { Settings = settings };
            control.Load();
            var grid = (Grid)control.ItemsPanelRoot!;
            Window.Current = new Window();
            settings.Spacing = 11;
            settings.Rows[0].Value = 3;
            settings.Columns.Add(new GridElement());
            Check(grid.RowSpacing == 8 && grid.ColumnDefinitions.Count == 0, "Foreign view changed the panel directly.");
            Window.Current = owner;
            control.Dispatcher.Drain();
            Check(grid.RowSpacing == 11 && grid.RowDefinitions[0].Height.Value == 3 && grid.ColumnDefinitions.Count == 1,
                "Queued refresh did not apply the latest state.");
            Window.Current = new Window();
            settings.Spacing = 14;
            Window.Current = owner;
            control.Unload();
            control.Load();
            var definition = grid.RowDefinitions[0];
            control.Dispatcher.Drain();
            Check(ReferenceEquals(definition, grid.RowDefinitions[0]), "Callback from the previous load was applied.");
            Window.Current = new Window();
            settings.Rows.Add(new GridElement());
            Window.Current = owner;
            var next = new LayerSettings { Kind = LayoutKind.Grid, Rows = [new GridElement { Value = 9 }] };
            control.Settings = next;
            definition = grid.RowDefinitions[0];
            control.Dispatcher.Drain();
            Check(ReferenceEquals(definition, grid.RowDefinitions[0]) && grid.RowDefinitions.Count == 1,
                "Callback from detached settings was applied.");
            control.Unload();
        });

        run("Settings without property notification support initial assignment and replacement", () =>
        {
            var control = new LayoutControl { Settings = new PlainLayoutSettings(LayoutKind.StackPanel, 13) };
            control.Load();
            Check(control.ItemsPanelRoot is StackPanel { Spacing: 13 }, "Plain settings were not applied.");
            control.Settings = new PlainLayoutSettings(LayoutKind.WrapPanel, 17);
            Check(control.ItemsPanelRoot is WrapPanel { HorizontalSpacing: 17, VerticalSpacing: 17 }, "Plain settings replacement failed.");
            control.Unload();
        });

        run("Unloaded controls can be collected while their settings, collections and elements remain alive", () =>
        {
            var settings = new LayerSettings
            {
                Kind = LayoutKind.Grid,
                Rows = [new GridElement()],
                Columns = [new GridElement()]
            };
            var reference = CreateUnloadedControl(settings);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Check(!reference.IsAlive, "External model events retained the unloaded control.");
            GC.KeepAlive(settings);
        });

        run("Grid defaults and explicit zeros survive JSON save, load and configuration copy", () =>
        {
            var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "layout.json");
            var settings = new LayerSettings
            {
                Kind = LayoutKind.Grid,
                Rows = [new GridElement()],
                Columns = [new GridElement { Value = 0, MaxValue = 0 }]
            };
            ConfigureFileHelper.SaveConfig(path, settings);
            foreach (var restored in new[]
            {
                ConfigureFileHelper.LoadConfigUnWrapped<LayerSettings>(path),
                ConfigureFileHelper.CopyObject(settings)
            })
            {
                Check(restored.Rows[0].Value == 1 && double.IsPositiveInfinity(restored.Rows[0].MaxValue)
                    && restored.Columns[0].Value == 0 && restored.Columns[0].MaxValue == 0, "Defaults or explicit zeros failed to round-trip.");
            }

            var legacy = JsonSerializer.Deserialize<GridElement>("{\"Value\":0,\"MaxValue\":0}")!;
            Check(legacy.Value == 0 && legacy.MaxValue == 0, "Existing explicit zero settings were migrated.");
            Check(JsonSerializer.Deserialize<GridElement>("{}") is { Value: 1 } missing
                && double.IsPositiveInfinity(missing.MaxValue), "Missing values did not use model defaults.");
            try
            {
                JsonSerializer.Serialize(double.PositiveInfinity);
                throw new Exception("Global floating-point serialization policy was changed.");
            }
            catch (ArgumentException)
            {
                // Only the GridElement.MaxValue property allows named floating-point literals.
            }
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateUnloadedControl(ILayoutSettings settings)
    {
        var control = new LayoutControl { Settings = settings };
        control.Load();
        control.Unload();
        return new WeakReference(control);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private sealed class NotifyingLayoutSettings : ILayoutSettings, INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _propertyChanged;

        public int Subscribers => _propertyChanged?.GetInvocationList().Length ?? 0;
        public LayoutKind Kind { get; set; }
        public double Spacing { get; set; }
        public ObservableCollection<GridElement> Rows { get; set; } = [];
        public ObservableCollection<GridElement> Columns { get; set; } = [];
        public Orientation Orientation { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => _propertyChanged += value;
            remove => _propertyChanged -= value;
        }

        public void NotifyAll() => _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private sealed class PlainLayoutSettings(LayoutKind kind, double spacing) : ILayoutSettings
    {
        public LayoutKind Kind => kind;
        public double Spacing => spacing;
        public ObservableCollection<GridElement> Rows { get; } = [];
        public ObservableCollection<GridElement> Columns { get; } = [];
        public Orientation Orientation => Orientation.Horizontal;
    }
}
