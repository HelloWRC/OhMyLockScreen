// These declarations validate panel configuration.
// Measurement, arrangement and XAML parsing require the real UWP runtime.
namespace Windows.UI.Xaml
{
    public sealed class DataTemplate(Func<DependencyObject>? createContent = null)
    {
        public DependencyObject LoadContent() => createContent?.Invoke() ?? new FrameworkElement();
    }

    public enum GridUnitType { Auto, Pixel, Star }

    public readonly record struct GridLength(double Value, GridUnitType GridUnitType)
    {
        public static GridLength Auto => new(1, GridUnitType.Auto);
    }
}

namespace Windows.UI.Xaml.Controls
{
    // Panel realization is explicit test scaffolding. Item generation and data binding
    // remain the responsibility of the real ItemsControl runtime, not this harness.
    public class ItemsControl : Control
    {
        private ItemsPanelTemplate? _itemsPanel;

        public global::System.Collections.ObjectModel.ObservableCollection<object> Items { get; } = [];
        public object? ItemsSource { get; set; }
        public DataTemplate? ItemTemplate { get; set; }
        public Panel? ItemsPanelRoot { get; private set; }
        public bool DeferPanelCreation { get; set; }

        // Explicit hooks exercise the control's overrides without emulating UWP item generation.
        internal DependencyObject CreateItemContainer(object item)
            => IsItemItsOwnContainerOverride(item) ? (DependencyObject)item : GetContainerForItemOverride();

        internal void PrepareItemContainer(DependencyObject element, object item)
            => PrepareContainerForItemOverride(element, item);

        internal void ClearItemContainer(DependencyObject element, object item)
            => ClearContainerForItemOverride(element, item);

        protected virtual DependencyObject GetContainerForItemOverride() => new ContentPresenter();

        protected virtual bool IsItemItsOwnContainerOverride(object item) => item is UIElement;

        protected virtual void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            if (element is ContentPresenter presenter && !ReferenceEquals(element, item))
            {
                presenter.Content = item;
            }
        }

        protected virtual void ClearContainerForItemOverride(DependencyObject element, object item)
        {
            if (element is ContentPresenter presenter && !ReferenceEquals(element, item))
            {
                presenter.Content = null;
            }
        }

        public ItemsPanelTemplate? ItemsPanel
        {
            get => _itemsPanel;
            set
            {
                _itemsPanel = value;
                if (!DeferPanelCreation)
                {
                    RealizeItemsPanel();
                }
            }
        }

        public void RealizeItemsPanel()
        {
            ItemsPanelRoot = _itemsPanel?.CreatePanel();
            RaiseLayoutUpdated();
        }
    }

    public sealed class ItemsPanelTemplate(Func<Panel> createPanel)
    {
        internal Panel CreatePanel() => createPanel();
    }

    public enum Orientation { Vertical, Horizontal }

    public class Panel : FrameworkElement;

    public sealed class Canvas : Panel;

    public sealed class StackPanel : Panel
    {
        public Orientation Orientation { get; set; }
        public double Spacing { get; set; }
    }

    public sealed class Grid : Panel
    {
        public static readonly DependencyProperty RowProperty = RegisterPosition("Row", 0);
        public static readonly DependencyProperty ColumnProperty = RegisterPosition("Column", 0);
        public static readonly DependencyProperty RowSpanProperty = RegisterPosition("RowSpan", 1);
        public static readonly DependencyProperty ColumnSpanProperty = RegisterPosition("ColumnSpan", 1);

        private static DependencyProperty RegisterPosition(string name, int minimum)
        {
            return DependencyProperty.Register(name, typeof(int), typeof(Grid),
                new PropertyMetadata(minimum, (_, args) =>
                {
                    if ((int)args.NewValue! < minimum)
                    {
                        throw new ArgumentException($"{name} must be at least {minimum}.");
                    }
                }));
        }

        public List<RowDefinition> RowDefinitions { get; } = [];
        public List<ColumnDefinition> ColumnDefinitions { get; } = [];
        public double RowSpacing { get; set; }
        public double ColumnSpacing { get; set; }

    }

    public sealed class RowDefinition
    {
        public GridLength Height { get; set; } = new(1, GridUnitType.Star);
        public double MinHeight { get; set; }
        public double MaxHeight { get; set; } = double.PositiveInfinity;
    }

    public sealed class ColumnDefinition
    {
        public GridLength Width { get; set; } = new(1, GridUnitType.Star);
        public double MinWidth { get; set; }
        public double MaxWidth { get; set; } = double.PositiveInfinity;
    }
}

namespace CommunityToolkit.WinUI.Controls
{
    public sealed class WrapPanel : Windows.UI.Xaml.Controls.Panel
    {
        public Windows.UI.Xaml.Controls.Orientation Orientation { get; set; }
        public double HorizontalSpacing { get; set; }
        public double VerticalSpacing { get; set; }
    }
}

namespace OhMyLockScreen.Shared.Controls.Scene
{
    public sealed partial class LayoutControl
    {
        private void InitializeComponent()
        {
            Resources["ComponentContainerTemplate"] = new Windows.UI.Xaml.DataTemplate(
                () => new ComponentPresenter());
            Resources["CanvasItemsPanel"] = new Windows.UI.Xaml.Controls.ItemsPanelTemplate(
                () => new Windows.UI.Xaml.Controls.Canvas());
            Resources["StackPanelItemsPanel"] = new Windows.UI.Xaml.Controls.ItemsPanelTemplate(
                () => new Windows.UI.Xaml.Controls.StackPanel());
            Resources["WrapPanelItemsPanel"] = new Windows.UI.Xaml.Controls.ItemsPanelTemplate(
                () => new CommunityToolkit.WinUI.Controls.WrapPanel());
            Resources["GridItemsPanel"] = new Windows.UI.Xaml.Controls.ItemsPanelTemplate(
                () => new Windows.UI.Xaml.Controls.Grid());
        }
    }

    public sealed partial class ScenePresenter
    {
        private void InitializeComponent() { }
    }
}

