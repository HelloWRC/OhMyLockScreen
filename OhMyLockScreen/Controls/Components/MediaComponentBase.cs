using System.ComponentModel;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.Media;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Models.Media;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Controls.Components;

public abstract class MediaComponentBase<TSettings> : ComponentBase<TSettings>
    where TSettings : MediaSettingsBase, new()
{
    private readonly MediaViewState _view;
    private TSettings? _observed;
    private bool _loaded;
    private bool _hasSettings;
    protected IMediaStateService MediaService { get; }
    protected MediaState State => _view.State;
    protected bool IsPlaceholder => _view.IsPlaceholder;

    protected MediaComponentBase(IMediaStateService service)
    {
        MediaService = service;
        _view = new MediaViewState(service, Dispatcher);
        _view.Changed += (_, _) => Refresh();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnSettingsChanged()
    {
        _hasSettings = true;
        ObserveSettings();
        Refresh();
    }

    protected override void OnIsEditingChanged() => Refresh();

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        _view.Activate();
        ObserveSettings();
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        _view.Dispose();
        ObserveSettings();
    }

    private void ObserveSettings()
    {
        if (_observed != null)
        {
            _observed.PropertyChanged -= OnSettingsPropertyChanged;
        }

        _observed = _loaded && _hasSettings ? Settings : null;
        if (_observed != null)
        {
            _observed.PropertyChanged += OnSettingsPropertyChanged;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args) => _view.RequestRefresh();

    protected void Refresh()
    {
        if (!_hasSettings)
        {
            return;
        }

        _view.IsEditing = IsEditing;
        Visibility = Settings.HideWhenNoMedia && !State.HasSession ? Visibility.Collapsed : Visibility.Visible;
        UpdateMedia();
    }

    protected abstract void UpdateMedia();
}
