using System.ComponentModel;
using CommunityToolkit.WinUI.Controls;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Controls.Editor;

public sealed partial class LayoutSettingsControl : LayoutSettingsControlBase
{
    private bool _loaded;
    private bool _settingsAttached;
    private bool _updatingAlignment;
    private LayoutFeatureSettings? _settings;
    private FrameworkElement? _sizeTarget;
    public LayoutMarginsViewModel? Margins
    {
        get; private set;
    }
    public int HorizontalAlignmentIndex => _settings == null
        ? -1
        : GetEnumIndex(_settings.HorizontalAlignment);
    public int VerticalAlignmentIndex => _settings == null
        ? -1
        : GetEnumIndex(_settings.VerticalAlignment);

    public LayoutSettingsControl()
    {
        InitializeComponent();
        RegisterPropertyChangedCallback(TargetElementProperty, OnTargetElementChanged);
    }

    protected override void OnSettingsChanged()
    {
        DetachSettings();
        _settings = Settings;
        Margins?.Dispose();
        Margins = new LayoutMarginsViewModel(Settings);
        if (_loaded)
        {
            Margins.Attach();
            AttachSettings();
            SynchronizeAutoSize();
        }

        OnPropertyChanged(nameof(Margins));
        RefreshAlignmentControls();
        Bindings.Update();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        Margins?.Attach();
        AttachSettings();
        RefreshSizeTarget();
        RefreshAlignmentControls();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        RefreshSizeTarget();
        DetachSettings();
        Margins?.Dispose();
    }

    private void AttachSettings()
    {
        if (_settingsAttached || _settings == null)
        {
            return;
        }

        _settings.PropertyChanged += OnSettingsPropertyChanged;
        _settingsAttached = true;
    }

    private void DetachSettings()
    {
        if (!_settingsAttached || _settings == null)
        {
            return;
        }

        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        _settingsAttached = false;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName)
            || args.PropertyName is nameof(LayoutFeatureSettings.IsAutoWidth)
                or nameof(LayoutFeatureSettings.IsAutoHeight))
        {
            SynchronizeAutoSize();
        }

        if (string.IsNullOrEmpty(args.PropertyName)
            || args.PropertyName is nameof(LayoutFeatureSettings.HorizontalAlignment)
                or nameof(LayoutFeatureSettings.VerticalAlignment))
        {
            RefreshAlignmentControls();
        }
    }

    private void OnTargetElementChanged(DependencyObject sender, DependencyProperty property) => RefreshSizeTarget();

    private void RefreshSizeTarget()
    {
        var target = _loaded ? TargetElement : null;
        if (!ReferenceEquals(_sizeTarget, target))
        {
            if (_sizeTarget != null)
            {
                _sizeTarget.Loaded -= OnSizeTargetLoaded;
                _sizeTarget.SizeChanged -= OnSizeTargetSizeChanged;
            }

            _sizeTarget = target;
            if (_sizeTarget != null)
            {
                _sizeTarget.Loaded += OnSizeTargetLoaded;
                _sizeTarget.SizeChanged += OnSizeTargetSizeChanged;
            }
        }

        SynchronizeAutoSize();
    }

    private void OnSizeTargetLoaded(object sender, RoutedEventArgs args) => SynchronizeAutoSize();

    private void OnSizeTargetSizeChanged(object sender, SizeChangedEventArgs args) => SynchronizeAutoSize();

    private void SynchronizeAutoSize()
    {
        if (!_loaded || _settings == null || _sizeTarget is not { IsLoaded: true } target)
        {
            return;
        }

        if (_settings.IsAutoWidth && double.IsFinite(target.ActualWidth) && target.ActualWidth > 0)
        {
            _settings.Width = SceneLayoutEditing.Round(target.ActualWidth);
        }

        if (_settings.IsAutoHeight && double.IsFinite(target.ActualHeight) && target.ActualHeight > 0)
        {
            _settings.Height = SceneLayoutEditing.Round(target.ActualHeight);
        }
    }

    private void RefreshAlignmentControls()
    {
        _updatingAlignment = true;
        try
        {
            OnPropertyChanged(nameof(HorizontalAlignmentIndex));
            OnPropertyChanged(nameof(VerticalAlignmentIndex));
        }
        finally
        {
            _updatingAlignment = false;
        }
    }

    private static int GetEnumIndex<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return Array.IndexOf(Enum.GetValues<TEnum>(), value);
    }

    private void OnHorizontalAlignmentSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_loaded && !_updatingAlignment
            && sender is Segmented { SelectedIndex: var index }
            && DataContext is LayoutFeatureSettings settings
            && TryGetEnumValue<HorizontalAlignment>(index, out var alignment))
        {
            settings.HorizontalAlignment = alignment;
        }
    }

    private void OnVerticalAlignmentSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_loaded && !_updatingAlignment
            && sender is Segmented { SelectedIndex: var index }
            && DataContext is LayoutFeatureSettings settings
            && TryGetEnumValue<VerticalAlignment>(index, out var alignment))
        {
            settings.VerticalAlignment = alignment;
        }
    }

    private static bool TryGetEnumValue<TEnum>(int index, out TEnum value)
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        if ((uint)index < (uint)values.Length)
        {
            value = values[index];
            return true;
        }

        value = default;
        return false;
    }
}
