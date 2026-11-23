using System.ComponentModel;
using OhMyLockScreen.ViewModels;
using Windows.Globalization.NumberFormatting;
using Windows.UI;
using Windows.UI.Xaml;
using Muxc = Microsoft.UI.Xaml.Controls;

namespace OhMyLockScreen.Controls.Editor;

public sealed partial class ShadowSettingsControl : ShadowSettingsControlBase
{
    private bool _loaded;
    private bool _synchronizingColor;

    public ShadowSettingsViewModel? Values { get; private set; }

    public ShadowSettingsControl()
    {
        InitializeComponent();

        // NumberBox 自带本地默认格式化器，样式 Setter 无法替换它。
        OpacityInput.NumberFormatter = new PercentFormatter
        {
            FractionDigits = 0,
            NumberRounder = new IncrementNumberRounder
            {
                Increment = 0.001,
                RoundingAlgorithm = RoundingAlgorithm.RoundHalfAwayFromZero
            }
        };

        var decimalFormatter = new DecimalFormatter
        {
            FractionDigits = 0,
            IsGrouped = false,
            NumberRounder = new IncrementNumberRounder
            {
                Increment = 0.01,
                RoundingAlgorithm = RoundingAlgorithm.RoundHalfAwayFromZero
            }
        };
        BlurRadiusInput.NumberFormatter = decimalFormatter;
        OffsetXInput.NumberFormatter = decimalFormatter;
        OffsetYInput.NumberFormatter = decimalFormatter;
    }

    protected override void OnSettingsChanged()
    {
        if (Values != null)
        {
            Values.PropertyChanged -= OnValuesPropertyChanged;
            Values.Dispose();
        }

        Values = SettingsInternal == null ? null : new ShadowSettingsViewModel(Settings);
        if (Values != null)
        {
            Values.PropertyChanged += OnValuesPropertyChanged;
            if (_loaded)
            {
                Values.Attach();
            }
        }

        UpdateColorSwatch();
        OnPropertyChanged(nameof(Values));
        Bindings.Update();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        Values?.Attach();
        UpdateColorSwatch();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        Values?.Dispose();
    }

    private void OnValuesPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(ShadowSettingsViewModel.Color))
        {
            UpdateColorSwatch();
        }
    }

    private void OnColorFlyoutOpening(object sender, object args)
    {
        if (Values == null)
        {
            return;
        }

        _synchronizingColor = true;
        try
        {
            var color = Values.Color;
            ShadowColorPicker.Color = Color.FromArgb(255, color.R, color.G, color.B);
        }
        finally
        {
            _synchronizingColor = false;
        }
    }

    private void OnColorPickerColorChanged(Muxc.ColorPicker sender, Muxc.ColorChangedEventArgs args)
    {
        if (_synchronizingColor || Values == null)
        {
            return;
        }

        Values.Color = Color.FromArgb(255, args.NewColor.R, args.NewColor.G, args.NewColor.B);
    }

    private void UpdateColorSwatch()
    {
        var color = Values?.Color ?? Colors.Black;
        ColorSwatchBrush.Color = Color.FromArgb(255, color.R, color.G, color.B);
    }
}
