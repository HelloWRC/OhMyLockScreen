using OhMyLockScreen.ViewModels;
using Windows.Globalization.NumberFormatting;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Controls.Editor;

public sealed partial class TransformSettingsControl : TransformSettingsControlBase
{
    private bool _loaded;

    public TransformSettingsViewModel? Values { get; private set; }

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
        Values?.Dispose();
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
