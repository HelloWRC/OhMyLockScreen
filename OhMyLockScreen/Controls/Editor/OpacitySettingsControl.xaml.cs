using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.ViewModels;
using Windows.Globalization.NumberFormatting;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Controls.Editor;

public sealed partial class OpacitySettingsControl : OpacitySettingsControlBase
{
    private bool _loaded;

    public IReadOnlyList<EditorChoice<OpacityBlendMode>> BlendModes { get; } =
        (EditorChoice<OpacityBlendMode>[])
        [
            new("正常", OpacityBlendMode.Normal),
            new("变暗", OpacityBlendMode.Darken),
            new("正片叠底", OpacityBlendMode.Multiply),
            new("颜色加深", OpacityBlendMode.ColorBurn),
            new("线性加深", OpacityBlendMode.LinearBurn),
            new("深色", OpacityBlendMode.DarkerColor),
            new("变亮", OpacityBlendMode.Lighten),
            new("滤色", OpacityBlendMode.Screen),
            new("颜色减淡", OpacityBlendMode.ColorDodge),
            new("线性减淡（添加）", OpacityBlendMode.LinearDodge),
            new("浅色", OpacityBlendMode.LighterColor),
            new("叠加", OpacityBlendMode.Overlay),
            new("柔光", OpacityBlendMode.SoftLight),
            new("强光", OpacityBlendMode.HardLight),
            new("亮光", OpacityBlendMode.VividLight),
            new("线性光", OpacityBlendMode.LinearLight),
            new("点光", OpacityBlendMode.PinLight),
            new("实色混合", OpacityBlendMode.HardMix),
            new("差值", OpacityBlendMode.Difference),
            new("排除", OpacityBlendMode.Exclusion),
            new("减去", OpacityBlendMode.Subtract),
            new("划分", OpacityBlendMode.Division)
        ];

    public OpacitySettingsViewModel? Values { get; private set; }

    public OpacitySettingsControl()
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
    }

    protected override void OnSettingsChanged()
    {
        Values?.Dispose();
        Values = SettingsInternal == null ? null : new OpacitySettingsViewModel(Settings);
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
