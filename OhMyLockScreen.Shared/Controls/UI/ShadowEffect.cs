using Windows.UI;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>描述用于轮廓投影的不可变阴影参数快照。</summary>
public sealed record ShadowEffectSpec(
    Color Color,
    double Opacity,
    double BlurRadius,
    double OffsetX,
    double OffsetY);
