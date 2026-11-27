using System.Numerics;
using OhMyLockScreen.Shared.Helpers.UI;
using Windows.Foundation;

namespace OhMyLockScreen.Shared.Controls.Scene;

/// <summary>提供视差目标偏移、三次贝塞尔缓动及视口约束的纯数学计算。</summary>
internal static class ParallaxMotionMath
{
    internal const double DefaultStrength = 100;
    internal const double DefaultAmount = 0.02;
    internal const double SaturationBase = 0.999;

    internal static readonly TimeSpan PositionDuration = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan ScaleDuration = TimeSpan.FromMilliseconds(1000);

    // 单段三次贝塞尔逼近 OutQuint（1 - (1 - u)^5），全区间最大误差约 0.8%。
    internal static readonly Vector2 OutQuintControlPoint1 = new(0.21f, 1.0f);
    internal static readonly Vector2 OutQuintControlPoint2 = new(0.37f, 1.0f);

    // 与 ScenePresenter.AddDoubleAnimation 的 KeySpline(0.1, 0.9, 0.2, 1) 保持一致。
    internal static readonly Vector2 UnlockControlPoint1 = new(0.1f, 0.9f);
    internal static readonly Vector2 UnlockControlPoint2 = new(0.2f, 1.0f);

    internal static double NormalizeStrength(double strength) =>
        double.IsFinite(strength) ? Math.Clamp(strength, 0, 200) : 0;

    internal static double? NormalizeStrengthOverride(double? strengthOverride)
    {
        if (strengthOverride is not { } value)
        {
            return null;
        }

        return double.IsFinite(value)
            ? Math.Clamp(value, 0, 200)
            : DefaultStrength;
    }

    internal static double GetAmount(double strength) =>
        DefaultAmount * (NormalizeStrength(strength) / 100);

    internal static Vector2 CalculateTargetOffset(Point? pointerInScene, double width, double height, double amount)
    {
        if (amount <= 0 || pointerInScene is not { } position || width <= 0 || height <= 0)
        {
            return Vector2.Zero;
        }

        // osu!lazer's desktop UI fits a 1024 × 768 logical area inside the viewport.
        var viewportScale = Math.Min(width / 1024, height / 768);
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        return new Vector2(
            CalculateAxisOffset(position.X - halfWidth, halfWidth, viewportScale, amount),
            CalculateAxisOffset(position.Y - halfHeight, halfHeight, viewportScale, amount));
    }

    internal static double EvaluateCubicBezier(double progress, Vector2 cp1, Vector2 cp2)
    {
        if (progress <= 0)
        {
            return 0;
        }

        if (progress >= 1)
        {
            return 1;
        }

        static double SampleBezier(double t, double p1, double p2)
        {
            var inv = 1 - t;
            return 3 * inv * inv * t * p1 + 3 * inv * t * t * p2 + t * t * t;
        }

        double low = 0;
        double high = 1;
        for (var i = 0; i < 48; i++)
        {
            var middle = (low + high) * 0.5;
            if (SampleBezier(middle, cp1.X, cp2.X) < progress)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return SampleBezier((low + high) * 0.5, cp1.Y, cp2.Y);
    }

    internal static (Vector2 Translation, double Scale) EvaluateEffectiveTransform(
        Vector2 offset,
        double scale,
        double maxAmount,
        double width,
        double height,
        double multiplier)
    {
        double clampedX;
        double clampedY;
        double baseScale;

        if (width > 0 && height > 0)
        {
            var margin = Math.Max(scale - 1, maxAmount) * 0.5;
            var limitX = width * margin;
            var limitY = height * margin;
            clampedX = Math.Clamp(offset.X, -limitX, limitX);
            clampedY = Math.Clamp(offset.Y, -limitY, limitY);
            baseScale = Math.Max(
                scale,
                1 + 2 * Math.Max(
                    Math.Abs(clampedX) / Math.Max(width, 0.0001),
                    Math.Abs(clampedY) / Math.Max(height, 0.0001)));
        }
        else
        {
            clampedX = 0;
            clampedY = 0;
            baseScale = scale;
        }

        var effectiveScale = 1 + (baseScale - 1) * multiplier;
        var translation = new Vector2((float)(clampedX * multiplier), (float)(clampedY * multiplier));
        return (translation, effectiveScale);
    }

    internal static BrushEditorMatrix CreateCenteredTransformMatrix(
        double width,
        double height,
        Vector2 translation,
        double scale)
    {
        if (Math.Abs(scale - 1) < 1e-9
            && Math.Abs(translation.X) < 1e-9
            && Math.Abs(translation.Y) < 1e-9)
        {
            return BrushEditorMatrix.Identity;
        }

        var cx = width * 0.5;
        var cy = height * 0.5;
        return new BrushEditorMatrix(
            scale,
            0,
            0,
            scale,
            cx * (1 - scale) + translation.X,
            cy * (1 - scale) + translation.Y);
    }

    private static float CalculateAxisOffset(double distance, double halfExtent, double viewportScale, double amount)
    {
        var saturation = 1 - Math.Pow(SaturationBase, Math.Abs(distance) / viewportScale);
        return (float)Math.CopySign(saturation * halfExtent * amount, distance);
    }
}
