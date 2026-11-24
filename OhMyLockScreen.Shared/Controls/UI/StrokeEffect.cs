using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>描边相对于原始轮廓的位置。</summary>
public enum StrokePosition
{
    Inside,
    Center,
    Outside
}

/// <summary>描述用于轮廓描边的不可变描边参数快照。</summary>
public sealed record StrokeEffectSpec(
    Brush? Brush,
    double Thickness,
    bool IsHollow,
    StrokePosition Position);

/// <summary>单步平滑边缘扩散与阈值恢复参数。</summary>
public readonly record struct StrokeMorphologyPass(
    float BlurAmount,
    float Gain,
    float DilateOffset,
    float ErodeOffset);

/// <summary>描述多级线性高斯阈值形态学的执行计划。</summary>
public sealed record StrokeMorphologyPlan(
    bool UsesHalfResolution,
    IReadOnlyList<StrokeMorphologyPass> Passes);

/// <summary>为纯 Composition 迭代高斯阈值形态学提供分桶边距与平滑步长规划。</summary>
public static class StrokeMorphologyPlanner
{
    private const double BaseThreshold = 0.08;
    // 满足 Phi(-BaseZ) = 0.08（单侧正态分布 8% 尾部位置）。
    private const double BaseZ = 1.4050715603096329;
    private const double MinSigma = 0.75;
    private const double HalfResThresholdPx = 24.0;
    private const double MaxHalfSigma = 12.0;
    private const double Pass0Sigma = 1.0;
    private const double PolishSigma = 2.0;
    private const double MinSignalGain = 0.52;

    private static readonly double BasePdf = Math.Exp(-0.5 * BaseZ * BaseZ) / Math.Sqrt(2.0 * Math.PI);
    private static readonly double Sqrt2 = Math.Sqrt(2.0);
    private static readonly double InvSqrt2Pi = 1.0 / Math.Sqrt(2.0 * Math.PI);
    private static readonly double[] FullResCaps = [1.0, 2.2, 4.6, 9.3];
    private static readonly double[] HalfResCaps = [2.0, 4.2, 8.4, 12.0];

    public static (double OuterRadius, double InnerRadius) GetEffectiveRadii(double thickness, StrokePosition position)
    {
        if (!double.IsFinite(thickness) || thickness <= 0)
        {
            return (0, 0);
        }

        return position switch
        {
            StrokePosition.Outside => (thickness, 0),
            StrokePosition.Center => (thickness / 2.0, thickness / 2.0),
            StrokePosition.Inside => (0, thickness),
            _ => (0, 0)
        };
    }

    public static double GetVisualOutset(StrokeEffectSpec? spec)
    {
        if (spec == null || spec.Brush == null || !double.IsFinite(spec.Thickness) || spec.Thickness <= 0)
        {
            return 0;
        }

        var (outerRadius, _) = GetEffectiveRadii(spec.Thickness, spec.Position);
        return outerRadius > 0 ? Math.Ceiling(outerRadius) + 2 : 0;
    }

    /// <summary>按离散档位分桶计算合成表面内边距，避免滑块拖动过程中频繁重建表面纹理。</summary>
    public static float GetSurfacePad(double outerRadius)
    {
        if (!double.IsFinite(outerRadius) || outerRadius <= 0)
        {
            return 2f;
        }

        return outerRadius switch
        {
            <= 6 => 10f,
            <= 16 => 20f,
            <= 32 => 36f,
            _ => 68f
        };
    }

    public static StrokeMorphologyPlan BuildPlan(double radiusDip, double rasterizationScale)
    {
        if (!double.IsFinite(radiusDip) || radiusDip <= 0)
        {
            return new StrokeMorphologyPlan(false, Array.Empty<StrokeMorphologyPass>());
        }

        var scale = double.IsFinite(rasterizationScale) && rasterizationScale > 0 ? rasterizationScale : 1.0;
        var radiusPx = radiusDip * scale;
        if (radiusPx <= 1e-4)
        {
            return new StrokeMorphologyPlan(false, Array.Empty<StrokeMorphologyPass>());
        }

        if (radiusPx <= HalfResThresholdPx)
        {
            var count = 1;
            var capSum = FullResCaps[0];
            while (count < FullResCaps.Length && radiusPx > capSum * BaseZ + 1e-4)
            {
                capSum += FullResCaps[count];
                count++;
            }

            var advScale = radiusPx / (capSum * BaseZ);
            var passes = new StrokeMorphologyPass[count];
            for (var i = 0; i < count; i++)
            {
                passes[i] = MakePass(FullResCaps[i] * advScale * BaseZ);
            }

            return new StrokeMorphologyPlan(false, passes);
        }

        var adv0 = Pass0Sigma * BaseZ;
        var advPolish = PolishSigma * BaseZ;
        var midHalfPx = Math.Max(1e-3, (radiusPx - adv0 - advPolish) * 0.5);

        var selectedCaps = new List<double>(6) { HalfResCaps[0] };
        var halfCapSum = HalfResCaps[0];
        var nextIndex = 1;
        while (midHalfPx > halfCapSum * BaseZ + 1e-4)
        {
            var nextCap = nextIndex < HalfResCaps.Length ? HalfResCaps[nextIndex++] : MaxHalfSigma;
            selectedCaps.Add(nextCap);
            halfCapSum += nextCap;
        }

        var midScale = midHalfPx / (halfCapSum * BaseZ);
        var halfPasses = new List<StrokeMorphologyPass>(selectedCaps.Count + 2)
        {
            MakePass(adv0)
        };
        foreach (var cap in selectedCaps)
        {
            halfPasses.Add(MakePass(cap * midScale * BaseZ));
        }

        halfPasses.Add(MakePass(advPolish));
        return new StrokeMorphologyPlan(true, halfPasses);
    }

    private static StrokeMorphologyPass MakePass(double advancePx)
    {
        var sigma = advancePx / BaseZ;
        double threshold;
        double pdf;
        if (sigma < MinSigma)
        {
            var z = Math.Max(0, advancePx / MinSigma);
            threshold = Math.Clamp(0.5 * Erfc(z / Sqrt2), 0.005, 0.5);
            pdf = Math.Exp(-0.5 * z * z) * InvSqrt2Pi;
            sigma = MinSigma;
        }
        else
        {
            threshold = BaseThreshold;
            pdf = BasePdf;
        }

        // 保证 0 输入映射 <= 0（无灰雾）且 1 输入映射 >= 1（实心满幅），同时恢复单位像素斜率。
        var gain = Math.Max(sigma / pdf, MinSignalGain / Math.Min(threshold, 1.0 - threshold));
        var dilateOffset = 0.5 - threshold * gain;
        var erodeOffset = 0.5 - (1.0 - threshold) * gain;
        return new StrokeMorphologyPass((float)sigma, (float)gain, (float)dilateOffset, (float)erodeOffset);
    }

    private static double Erfc(double x)
    {
        var absX = Math.Abs(x);
        var t = 1.0 / (1.0 + 0.3275911 * absX);
        var poly = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))));
        var value = poly * Math.Exp(-absX * absX);
        return x >= 0 ? value : 2.0 - value;
    }
}
