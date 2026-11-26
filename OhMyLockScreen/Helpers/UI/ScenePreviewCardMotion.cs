using System.Numerics;

namespace OhMyLockScreen.Helpers.UI;

/// <summary>定义场景管理器卡片的遮罩展开、标题滑移及悬停弹簧位移参数。</summary>
public static class ScenePreviewCardMotion
{
    public const float CardCornerRadius = 8.0f;
    public const float DefaultCardHeight = 200.0f;
    public const float MaskFadeHeight = 54.0f;
    public const float HeaderTopPadding = 8.0f;
    public const float RestTitleBottomPadding = 4.0f;
    public const float TitleLineHeight = 16.0f;

    public const float HoverMaskOffsetY = -MaskFadeHeight;
    public const float HoverHeaderOffsetY = 0.0f;

    public const float RestLiftOffsetY = 0.0f;
    public const float HoverLiftOffsetY = -2.0f;
    public const float PressedLiftOffsetY = -0.75f;
    public const float LiftSpringDampingRatio = 0.65f;
    public static readonly TimeSpan LiftSpringPeriod = TimeSpan.FromMilliseconds(50);

    // 与 Figma Smart Animate 参数一致：进入 333ms cubic-bezier(0, 0, 0, 1)，离开 167ms cubic-bezier(1, 0, 1, 1)。
    public static readonly TimeSpan HoverEnterDuration = TimeSpan.FromMilliseconds(333);
    public static readonly Vector2 HoverEnterControlPoint1 = new(0.0f, 0.0f);
    public static readonly Vector2 HoverEnterControlPoint2 = new(0.0f, 1.0f);

    public static readonly TimeSpan HoverLeaveDuration = TimeSpan.FromMilliseconds(167);
    public static readonly Vector2 HoverLeaveControlPoint1 = new(1.0f, 0.0f);
    public static readonly Vector2 HoverLeaveControlPoint2 = new(1.0f, 1.0f);

    public static float GetRestMaskOffsetY(float cardHeight) =>
        MathF.Max(0f, cardHeight - MaskFadeHeight);

    public static float GetRestHeaderOffsetY(float cardHeight) =>
        MathF.Max(0f, cardHeight - RestTitleBottomPadding - TitleLineHeight - HeaderTopPadding);

    public static float GetTargetLiftOffsetY(bool isExpanded, bool isPressed, bool animationsEnabled)
    {
        if (!animationsEnabled)
        {
            return RestLiftOffsetY;
        }

        if (isExpanded && isPressed)
        {
            return PressedLiftOffsetY;
        }

        return isExpanded ? HoverLiftOffsetY : RestLiftOffsetY;
    }
}
