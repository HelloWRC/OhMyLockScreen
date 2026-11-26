using OhMyLockScreen.ViewModels;
using Windows.Globalization.NumberFormatting;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Controls.Editor;

public sealed partial class TransformSettingsControl : TransformSettingsControlBase
{
    private bool _loaded;

    public TransformSettingsViewModel? Values { get; private set; }

    public bool IsScaleFieldFocused { get; private set; }

    public event EventHandler? ScaleFieldFocusChanged;

    public TransformSettingsControl()
    {
        InitializeComponent();

        // NumberBox supplies a local default formatter, so a style setter cannot replace it.
        var degreeFormatter = new DegreeNumberFormatter();
        RotationInput.NumberFormatter = degreeFormatter;
        SkewXInput.NumberFormatter = degreeFormatter;
        SkewYInput.NumberFormatter = degreeFormatter;

        var percentFormatter = new PercentFormatter
        {
            FractionDigits = 0,
            NumberRounder = new IncrementNumberRounder
            {
                Increment = 0.001,
                RoundingAlgorithm = RoundingAlgorithm.RoundHalfAwayFromZero
            }
        };
        ScaleXInput.NumberFormatter = percentFormatter;
        ScaleYInput.NumberFormatter = percentFormatter;
        OriginXInput.NumberFormatter = percentFormatter;
        OriginYInput.NumberFormatter = percentFormatter;
    }

    protected override void OnSettingsChanged()
    {
        SetScaleFieldFocused(false);
        Values?.Dispose();
        Values = SettingsInternal == null ? null : new TransformSettingsViewModel(Settings);
        if (_loaded)
        {
            Values?.Attach();
        }

        OnPropertyChanged(nameof(Values));
        Bindings.Update();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _loaded = true;
        Values?.Attach();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _loaded = false;
        SetScaleFieldFocused(false);
        Values?.Dispose();
    }

    private void OnScaleFieldGotFocus(object sender, RoutedEventArgs args)
    {
        if (_loaded)
        {
            SetScaleFieldFocused(true);
        }
    }

    private void OnScaleFieldLostFocus(object sender, RoutedEventArgs args)
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, RefreshScaleFieldFocus);
    }

    private void RefreshScaleFieldFocus()
    {
        if (!_loaded)
        {
            SetScaleFieldFocused(false);
            return;
        }

        var focused = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) && XamlRoot != null
            ? FocusManager.GetFocusedElement(XamlRoot) as DependencyObject
            : FocusManager.GetFocusedElement() as DependencyObject;
        SetScaleFieldFocused(IsWithin(focused, ScaleXInput) || IsWithin(focused, ScaleYInput));
    }

    private void SetScaleFieldFocused(bool focused)
    {
        if (IsScaleFieldFocused == focused)
        {
            return;
        }

        IsScaleFieldFocused = focused;
        ScaleFieldFocusChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class DegreeNumberFormatter : INumberFormatter2, INumberParser
    {
        private readonly DecimalFormatter _inner = new()
        {
            FractionDigits = 0,
            IsGrouped = false,
            NumberRounder = new IncrementNumberRounder
            {
                Increment = 0.01,
                RoundingAlgorithm = RoundingAlgorithm.RoundHalfAwayFromZero
            }
        };

        public string FormatInt(long value) => $"{_inner.FormatInt(value)}°";
        public string FormatUInt(ulong value) => $"{_inner.FormatUInt(value)}°";
        public string FormatDouble(double value) => $"{_inner.FormatDouble(value)}°";

        public long? ParseInt(string text) => _inner.ParseInt(StripSuffix(text));
        public ulong? ParseUInt(string text) => _inner.ParseUInt(StripSuffix(text));
        public double? ParseDouble(string text) => _inner.ParseDouble(StripSuffix(text));

        private static string StripSuffix(string text) => text.Trim().TrimEnd('°', ' ');
    }
}
