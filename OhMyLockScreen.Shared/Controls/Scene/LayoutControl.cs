using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.Scene;

/// <summary>根据布局设置选择原生 ItemsPanel，通过 ComponentPresenter 项目容器呈现组件。</summary>
public sealed partial class LayoutControl : ItemsControl
{
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(
        nameof(Settings), typeof(ILayoutSettings), typeof(LayoutControl),
        new PropertyMetadata(null, OnSettingsChanged));

    private readonly HashSet<ObservableCollection<GridElement>> _subscribedCollections = [];
    private readonly HashSet<GridElement> _subscribedElements = [];
    private INotifyPropertyChanged? _subscribedSettings;
    private Panel? _configuredPanel;
    private LayoutKind? _panelKind;
    private bool _isLoaded;
    private int _subscriptionVersion;

    public ILayoutSettings? Settings
    {
        get => (ILayoutSettings?)GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    public LayoutControl()
    {
        InitializeComponent();
        RefreshLayout();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override DependencyObject GetContainerForItemOverride()
        => ((DataTemplate)Resources["ComponentContainerTemplate"]).LoadContent();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is ComponentPresenter;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        if (ReferenceEquals(element, item))
        {
            return;
        }

        // The presenter owns Content; the default preparation would assign the settings object to it.
        var presenter = (ComponentPresenter)element;
        presenter.DataContext = item;
        presenter.Settings = item as ComponentSettings;
    }

    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (ReferenceEquals(element, item))
        {
            return;
        }

        var presenter = (ComponentPresenter)element;
        presenter.Settings = null;
        presenter.DataContext = null;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _configuredPanel = null;
        UpdatePanelSettings();
    }

    private static void OnSettingsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((LayoutControl)sender).RefreshLayout();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        LayoutUpdated += OnLayoutUpdated;
        RefreshLayout();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = false;
        LayoutUpdated -= OnLayoutUpdated;
        _configuredPanel = null;
        DetachSubscriptions();
    }

    private void OnLayoutUpdated(object? sender, object args)
    {
        if (!ReferenceEquals(_configuredPanel, ItemsPanelRoot))
        {
            UpdatePanelSettings();
        }
    }

    private void RefreshLayout()
    {
        DetachSubscriptions();
        var settings = Settings;
        if (_isLoaded && settings != null)
        {
            _subscribedSettings = settings as INotifyPropertyChanged;
            if (_subscribedSettings != null)
            {
                _subscribedSettings.PropertyChanged += OnSettingsPropertyChanged;
            }

            SubscribeCollection(settings.Rows);
            SubscribeCollection(settings.Columns);
        }

        var kind = settings?.Kind ?? LayoutKind.Canvas;
        if (kind is not (LayoutKind.Canvas or LayoutKind.StackPanel or LayoutKind.WrapPanel or LayoutKind.Grid))
        {
            kind = LayoutKind.Canvas;
        }

        if (_panelKind != kind)
        {
            _panelKind = kind;
            _configuredPanel = null;
            ItemsPanel = (ItemsPanelTemplate)Resources[$"{kind}ItemsPanel"];
        }

        UpdatePanelSettings();
    }

    private void UpdatePanelSettings()
    {
        // ItemsControl creates the panel asynchronously when its ItemsPresenter applies the template.
        var panel = ItemsPanelRoot;
        var settings = Settings;
        switch (panel)
        {
            case Canvas when _panelKind == LayoutKind.Canvas:
                break;
            case StackPanel stackPanel when _panelKind == LayoutKind.StackPanel && settings != null:
                stackPanel.Orientation = settings.Orientation;
                stackPanel.Spacing = settings.Spacing;
                break;
            case WrapPanel wrapPanel when _panelKind == LayoutKind.WrapPanel && settings != null:
                wrapPanel.Orientation = settings.Orientation;
                wrapPanel.HorizontalSpacing = settings.Spacing;
                wrapPanel.VerticalSpacing = settings.Spacing;
                break;
            case Grid grid when _panelKind == LayoutKind.Grid && settings != null:
                grid.RowSpacing = settings.Spacing;
                grid.ColumnSpacing = settings.Spacing;
                UpdateGridDefinitions(grid, settings);
                break;
            default:
                return;
        }

        _configuredPanel = panel;
    }

    private static void UpdateGridDefinitions(Grid grid, ILayoutSettings settings)
    {
        grid.RowDefinitions.Clear();
        foreach (var row in settings.Rows)
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = GetGridLength(row),
                MinHeight = row.MinValue,
                MaxHeight = row.MaxValue
            });
        }

        grid.ColumnDefinitions.Clear();
        foreach (var column in settings.Columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GetGridLength(column),
                MinWidth = column.MinValue,
                MaxWidth = column.MaxValue
            });
        }
    }

    private static GridLength GetGridLength(GridElement element) => element.Mode switch
    {
        GridElementMode.Fixed => new GridLength(element.Value, GridUnitType.Pixel),
        GridElementMode.Auto => GridLength.Auto,
        _ => new GridLength(element.Value, GridUnitType.Star)
    };

    private void SubscribeCollection(ObservableCollection<GridElement> collection)
    {
        if (!_subscribedCollections.Add(collection))
        {
            return;
        }

        collection.CollectionChanged += OnGridCollectionChanged;
        foreach (var element in collection)
        {
            if (_subscribedElements.Add(element))
            {
                element.PropertyChanged += OnGridElementPropertyChanged;
            }
        }
    }

    private void DetachSubscriptions()
    {
        Interlocked.Increment(ref _subscriptionVersion);
        if (_subscribedSettings != null)
        {
            _subscribedSettings.PropertyChanged -= OnSettingsPropertyChanged;
            _subscribedSettings = null;
        }

        foreach (var collection in _subscribedCollections)
        {
            collection.CollectionChanged -= OnGridCollectionChanged;
        }

        foreach (var element in _subscribedElements)
        {
            element.PropertyChanged -= OnGridElementPropertyChanged;
        }

        _subscribedCollections.Clear();
        _subscribedElements.Clear();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName)
            || args.PropertyName is nameof(ILayoutSettings.Kind) or nameof(ILayoutSettings.Spacing)
                or nameof(ILayoutSettings.Orientation) or nameof(ILayoutSettings.Rows) or nameof(ILayoutSettings.Columns))
        {
            QueueRefresh(sender);
        }
    }

    private void OnGridCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => QueueRefresh(sender);

    private void OnGridElementPropertyChanged(object? sender, PropertyChangedEventArgs args) => QueueRefresh(sender);

    private bool IsSubscribed(object? sender) => ReferenceEquals(sender, _subscribedSettings)
        || sender is ObservableCollection<GridElement> collection && _subscribedCollections.Contains(collection)
        || sender is GridElement element && _subscribedElements.Contains(element);

    private async void QueueRefresh(object? sender)
    {
        var version = Volatile.Read(ref _subscriptionVersion);
        try
        {
            if (Dispatcher.HasThreadAccess)
            {
                RefreshIfCurrent();
            }
            else
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, RefreshIfCurrent);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法更新布局：{exception}");
            IAppHost.TryGetService<ILogger<LayoutControl>>()?.LogError(exception, "无法更新布局");
        }

        void RefreshIfCurrent()
        {
            if (_isLoaded && version == _subscriptionVersion && IsSubscribed(sender))
            {
                RefreshLayout();
            }
        }
    }
}
