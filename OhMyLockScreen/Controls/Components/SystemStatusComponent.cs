using System.ComponentModel;
using FluentIcons.Common;
using FluentIcons.Uwp;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.SystemStatus;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.SystemStatus;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.SystemStatus;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Controls.Components;

[ComponentInfo(Id, "系统状态", Icon.Battery5, "显示电量、网络、音量或蓝牙连接状态。")]
[ComponentDefaultFeatures(typeof(SystemStatusDefaultFeaturesProvider))]
public sealed class SystemStatusComponent : ComponentBase<SystemStatusComponentSettings>, ITextStyleTarget
{
    public const string Id = "omls.components.systemStatus";
    private readonly ISystemStatusService _service;
    private readonly Grid _root = new();
    private readonly FluentIcon _icon = new() { IconSize = IconSize.Resizable, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private SystemStatusComponentSettings? _observed;
    private IDisposable? _subscription;
    private SystemStatusKind? _kind;
    private bool _loaded;
    private bool _hasSettings;
    private int _generation;
    public FrameworkElement TextElement => _text;

    public SystemStatusComponent(ISystemStatusService service)
    {
        _service = service;
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_text, 1);
        _root.Children.Add(_icon);
        _root.Children.Add(_text);
        Content = _root;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnSettingsChanged()
    {
        _hasSettings = true;
        ObserveSettings();
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        ++_generation;
        _service.Changed += OnStateChanged;
        ObserveSettings();
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        ++_generation;
        _service.Changed -= OnStateChanged;
        _subscription?.Dispose();
        _subscription = null;
        _kind = null;
        ObserveSettings();
    }

    private void ObserveSettings()
    {
        if (_observed != null)
        {
            _observed.PropertyChanged -= OnSettingChanged;
        }

        _observed = _loaded && _hasSettings ? Settings : null;
        if (_observed != null)
        {
            _observed.PropertyChanged += OnSettingChanged;
        }
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs args) => RequestRefresh();

    private void OnStateChanged(object? sender, SystemStatusChangedEventArgs args)
    {
        // This callback can run on any thread; read dependency objects only in Refresh.
        RequestRefresh();
    }

    private async void RequestRefresh()
    {
        var generation = _generation;
        try
        {
            if (Dispatcher.HasThreadAccess)
            {
                Update();
            }
            else
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, Update);
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"系统状态视图已关闭：{exception}");
        }

        void Update()
        {
            if (_loaded && generation == _generation)
            {
                Refresh();
            }
        }
    }

    private void Refresh()
    {
        if (!_hasSettings)
        {
            return;
        }

        if (_loaded && _kind != Settings.Kind)
        {
            ++_generation;
            _subscription?.Dispose();
            _kind = Settings.Kind;
            _subscription = _service.Acquire(Settings.Kind);
        }

        var display = SystemStatusPresentation.Create(Settings.Kind, _service.GetState(Settings.Kind));
        _icon.Icon = display.Icon;
        _icon.FontSize = Settings.IconSize;
        _text.Text = display.Text;
        _icon.Visibility = Settings.ShowIcon ? Visibility.Visible : Visibility.Collapsed;
        _text.Visibility = Settings.ShowText ? Visibility.Visible : Visibility.Collapsed;
        _root.ColumnSpacing = Settings.ShowIcon && Settings.ShowText ? 6 : 0;
        AutomationProperties.SetName(this, Settings.ShowIcon || Settings.ShowText ? display.Text : "");
    }
}

public sealed class SystemStatusDefaultFeaturesProvider : IComponentDefaultFeaturesProvider
{
    public IEnumerable<FeatureInfo> CreateDefaultFeatures() => [TextDefaults.Size(16)];
}
