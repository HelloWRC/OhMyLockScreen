using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>描述目标视觉编组与后方内容的混合方式。</summary>
public enum OpacityBlendMode
{
    Normal = 0,

    // 变暗
    Darken = 1,
    Multiply = 2,
    ColorBurn = 3,
    LinearBurn = 4,
    DarkerColor = 5,

    // 变亮
    Lighten = 6,
    Screen = 7,
    ColorDodge = 8,
    LinearDodge = 9,
    LighterColor = 10,

    // 对比
    Overlay = 11,
    SoftLight = 12,
    HardLight = 13,
    VividLight = 14,
    LinearLight = 15,
    PinLight = 16,
    HardMix = 17,

    // 差值与反相
    Difference = 18,
    Exclusion = 19,
    Subtract = 20,
    Division = 21
}

/// <summary>描述用于整组不透明度与混合模式合成的不可变参数快照。</summary>
public sealed record OpacityEffectSpec(
    double Opacity,
    OpacityBlendMode BlendMode);

/// <summary>为不透明度与混合方式提供纯计算参数清洗及外扩边距规范化。</summary>
public static class OpacityEffectHelper
{
    public const double MaxOutset = 1024;

    public static double SanitizeOpacity(double value, double fallback = 1.0)
    {
        if (!double.IsFinite(value))
        {
            return fallback;
        }

        var clamped = Math.Clamp(value, 0, 1);
        return clamped == 0 ? 0 : clamped;
    }

    public static OpacityBlendMode SanitizeBlendMode(OpacityBlendMode mode)
        => Enum.IsDefined(mode) ? mode : OpacityBlendMode.Normal;

    public static Thickness NormalizeOutset(Thickness outset)
    {
        return new Thickness(
            NormalizeSide(outset.Left),
            NormalizeSide(outset.Top),
            NormalizeSide(outset.Right),
            NormalizeSide(outset.Bottom));
    }

    public static double GetMaxOutset(Thickness outset)
    {
        var normalized = NormalizeOutset(outset);
        return Math.Max(
            Math.Max(normalized.Left, normalized.Top),
            Math.Max(normalized.Right, normalized.Bottom));
    }

    private static double NormalizeSide(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return 0;
        }

        var ceiled = Math.Clamp(Math.Ceiling(value), 0, MaxOutset);
        return ceiled == 0 ? 0 : ceiled;
    }
}
