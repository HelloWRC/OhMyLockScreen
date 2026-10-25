using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.ViewModels;

public sealed class LayoutPanelSettingsViewModel : ObservableObject, IDisposable
{
    private ILayoutSettings? _model;
    private INotifyPropertyChanged? _notifier;
    private bool _isAttached;

    public ILayoutSettings? Model
    {
        get => _model;
        set
        {
            if (ReferenceEquals(_model, value))
            {
                return;
            }

            DetachModel();
            _model = value;
            if (_isAttached)
            {
                AttachModel();
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(KindIndex));
            OnPropertyChanged(nameof(Orientation));
            OnPropertyChanged(nameof(OrientationIndex));
            OnPropertyChanged(nameof(Spacing));
            RefreshCapabilities();
        }
    }

    public bool ShowOrientation => Model?.Kind is LayoutKind.StackPanel or LayoutKind.WrapPanel;
    public bool ShowSpacing => Model?.Kind is LayoutKind.StackPanel or LayoutKind.WrapPanel or LayoutKind.Grid;
    public bool ShowGridDefinitions => Model?.Kind == LayoutKind.Grid;
    public LayoutKind Kind
    {
        get => Model?.Kind ?? LayoutKind.Canvas;
        set
        {
            if (Model != null)
            {
                Model.Kind = value;
            }
        }
    }

    public int KindIndex
    {
        get => GetEnumIndex(Kind);
        set
        {
            if (Enum.IsDefined(typeof(LayoutKind), value))
            {
                Kind = (LayoutKind)value;
            }
        }
    }

    public Orientation Orientation
    {
        get => Model?.Orientation ?? Orientation.Vertical;
        set
        {
            if (Model != null)
            {
                Model.Orientation = value;
            }
        }
    }

    public int OrientationIndex
    {
        get => GetEnumIndex(Orientation);
        set
        {
            if (Enum.IsDefined(typeof(Orientation), value))
            {
                Orientation = (Orientation)value;
            }
        }
    }

    public double Spacing
    {
        get => Model?.Spacing ?? 0;
        set
        {
            if (Model != null && double.IsFinite(value))
            {
                Model.Spacing = value;
            }
        }
    }

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        AttachModel();
        RefreshCapabilities();
    }

    public void Dispose()
    {
        DetachModel();
        _isAttached = false;
    }

    private void AttachModel()
    {
        if (Model is not INotifyPropertyChanged notifier)
        {
            return;
        }

        _notifier = notifier;
        notifier.PropertyChanged += OnModelPropertyChanged;
    }

    private void DetachModel()
    {
        if (_notifier != null)
        {
            _notifier.PropertyChanged -= OnModelPropertyChanged;
            _notifier = null;
        }
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ILayoutSettings.Kind))
        {
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(KindIndex));
            RefreshCapabilities();
        }
        else if (args.PropertyName == nameof(ILayoutSettings.Orientation))
        {
            OnPropertyChanged(nameof(Orientation));
            OnPropertyChanged(nameof(OrientationIndex));
        }
        else if (args.PropertyName == nameof(ILayoutSettings.Spacing))
        {
            OnPropertyChanged(nameof(Spacing));
        }
    }

    private void RefreshCapabilities()
    {
        OnPropertyChanged(nameof(ShowOrientation));
        OnPropertyChanged(nameof(ShowSpacing));
        OnPropertyChanged(nameof(ShowGridDefinitions));
    }

    private static int GetEnumIndex<T>(T value) where T : struct, Enum
    {
        return Enum.IsDefined(typeof(T), value)
            ? Array.IndexOf(Enum.GetValues<T>(), value)
            : -1;
    }
}
