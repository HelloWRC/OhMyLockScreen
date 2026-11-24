using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.ViewModels;
using Windows.Globalization.NumberFormatting;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Controls.Editor;

public sealed partial class StrokeSettingsControl : StrokeSettingsControlBase
{
    private bool _loaded;

    public IReadOnlyList<EditorChoice<StrokePosition>> Positions { get; } =
        (EditorChoice<StrokePosition>[])
        [
            new("内部", StrokePosition.Inside),
            new("中心", StrokePosition.Center),
            new("外部", StrokePosition.Outside)
        ];

    public StrokeSettingsViewModel? Values { get; private set; }

    public StrokeSettingsControl()
    {
        InitializeComponent();

        // NumberBox 自带本地默认格式化器，样式 Setter 无法替换它。
        ThicknessInput.NumberFormatter = new DecimalFormatter
        {
            FractionDigits = 0,
            IsGrouped = false,
            NumberRounder = new IncrementNumberRounder
            {
                Increment = 0.01,
                RoundingAlgorithm = RoundingAlgorithm.RoundHalfAwayFromZero
            }
        };
    }

    protected override void OnSettingsChanged()
    {
        Values?.Dispose();
        Values = SettingsInternal == null ? null : new StrokeSettingsViewModel(Settings);
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
}
