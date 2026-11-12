using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.Scene;

/// <summary>根据组件配置创建并显示组件。每个 Presenter 拥有独立控件。</summary>
public sealed partial class ComponentPresenter : ContentPresenter, IBorderable, ISceneBlobContext
{
    public static readonly DependencyProperty BlobUriConverterProperty = DependencyProperty.Register(
        nameof(BlobUriConverter), typeof(SceneBlobUriConverter), typeof(ComponentPresenter),
        new PropertyMetadata(null, OnBlobContextChanged));

    public SceneBlobUriConverter? BlobUriConverter
    {
        get => (SceneBlobUriConverter?)GetValue(BlobUriConverterProperty);
        set => SetValue(BlobUriConverterProperty, value);
    }

    private static void OnBlobContextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (ComponentPresenter)sender;
        if (presenter.Content is ComponentBase component)
        {
            component.BlobUriConverter = presenter.BlobUriConverter;
        }

        presenter._featureAttacher?.Reload();
    }

    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(
        nameof(Settings), typeof(ComponentSettings), typeof(ComponentPresenter),
        new PropertyMetadata(null, OnSettingsChanged));

    private ComponentSettings? _subscribedSettings;
    private FeatureAttacher? _featureAttacher;
    private bool _isLoaded;
    private bool _isUpdating;
    private bool _preserveContentOnUnload;
    private Border? _border;

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    public Border? Border
    {
        get => _border;
        private set
        {
            if (ReferenceEquals(_border, value))
            {
                return;
            }

            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(nameof(Border)));
            _border = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Border)));
        }
    }

    public ComponentSettings? Settings
    {
        get => (ComponentSettings?)GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    public ComponentPresenter()
    {
        InitializeComponent();
        RegisterPropertyChangedCallback(ContentProperty, (_, _) => UpdateContentVisibility());
        RegisterPropertyChangedCallback(ForegroundProperty, OnForegroundChanged);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static void OnSettingsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (ComponentPresenter)sender;
        if (!presenter._isLoaded)
        {
            return;
        }

        presenter.UpdateSubscription(presenter.Settings);
        presenter.UpdateContent();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Activate();
    }

    internal void ActivateFromLayout() => Activate();
    internal void PreserveContentForItemsPanelChange() => _preserveContentOnUnload = true;

    private void Activate()
    {
        if (_isLoaded)
        {
            return;
        }

        _preserveContentOnUnload = false;
        _isLoaded = true;
        LayoutUpdated += OnForegroundLayoutUpdated;
        UpdateSubscription(Settings);
        UpdateContent();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = false;
        ClearContentVisibility();
        LayoutUpdated -= OnForegroundLayoutUpdated;
        Border = null;
        UpdateSubscription(null);
        var attacher = _featureAttacher;
        _featureAttacher = null;
        attacher?.Dispose();
        if (!_preserveContentOnUnload)
        {
            Content = null;
        }

        ClearContentForeground();
    }

    private void OnDecorationBorderLoaded(object sender, RoutedEventArgs args)
    {
        Border = (Border)sender;
    }

    private void OnDecorationBorderUnloaded(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(Border, sender))
        {
            Border = null;
        }
    }

    private void AttachFeatures()
    {
        if (!_isLoaded)
        {
            return;
        }

        _featureAttacher ??= new FeatureAttacher(this);
        _featureAttacher.FeatureContainer = Settings;
        _featureAttacher.Attach();
    }

    private void UpdateSubscription(ComponentSettings? settings)
    {
        if (_subscribedSettings != null)
        {
            _subscribedSettings.PropertyChanged -= OnSettingsPropertyChanged;
        }

        _subscribedSettings = settings;
        if (settings != null)
        {
            settings.PropertyChanged += OnSettingsPropertyChanged;
        }
    }

    private async void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!string.IsNullOrEmpty(args.PropertyName)
            && args.PropertyName is not nameof(ComponentSettings.Id) and not nameof(ComponentSettings.Settings))
        {
            return;
        }

        try
        {
            if (Dispatcher.HasThreadAccess)
            {
                UpdateContent();
            }
            else
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // A queued change may belong to a model that has since been detached.
                    if (ReferenceEquals(sender, _subscribedSettings))
                    {
                        UpdateContent();
                    }
                });
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法调度组件更新：{exception}");
            IAppHost.TryGetService<ILogger<ComponentPresenter>>()?
                .LogError(exception, "无法调度组件更新");
        }
    }

    private void UpdateContent()
    {
        if (!_isLoaded || _isUpdating)
        {
            return;
        }

        _isUpdating = true;
        try
        {
            _featureAttacher?.Detach();
            if (!_isLoaded)
            {
                return;
            }

            Content = null;
            if (Settings != null)
            {
                var component = IAppHost.GetRequiredService<IComponentsService>().CreateComponent(Settings);
                if (component != null)
                {
                    component.BlobUriConverter = BlobUriConverter;
                }
                Content = component;
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法创建组件 {Settings?.Id}：{exception}");
            IAppHost.TryGetService<ILogger<ComponentPresenter>>()?
                .LogError(exception, "无法创建组件：{ComponentId}", Settings?.Id);
        }
        finally
        {
            _isUpdating = false;
        }

        AttachFeatures();
        UpdateContentVisibility();
        RefreshContentForeground(force: true);
    }
}
