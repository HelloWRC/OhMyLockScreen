namespace OhMyLockScreen.Shared.Helpers.UI;

/// <summary>与 ClassIsland 相同的水平手势阈值与不可关闭消息阻尼。</summary>
internal static class ToastSwipeGesture
{
    internal static double Threshold(double width) => Math.Clamp(width * 0.25, 48, 96);
    internal static bool IsVertical(double x, double y) => Math.Abs(y) > 8 && Math.Abs(y) >= Math.Abs(x);
    internal static bool IsHorizontal(double x, double y) => Math.Abs(x) > 8 && Math.Abs(x) > Math.Abs(y);

    internal static bool ShouldDismiss(double offset, double velocity, double width, bool canUserClose) =>
        canUserClose && (Math.Abs(offset) >= Threshold(width) ||
            Math.Abs(offset) >= 16 && Math.Abs(velocity) >= 800 && Math.Sign(offset) == Math.Sign(velocity));

    internal static double Resist(double offset, double width)
    {
        var threshold = Threshold(width);
        return Math.Sign(offset) * Math.Min(threshold - 0.01, threshold * (1 - Math.Exp(-Math.Abs(offset) / threshold)));
    }

    internal static double Unresist(double offset, double width)
    {
        var threshold = Threshold(width);
        return Math.Sign(offset) * -threshold * Math.Log(Math.Max(0.001, 1 - Math.Abs(offset) / threshold));
    }
}
