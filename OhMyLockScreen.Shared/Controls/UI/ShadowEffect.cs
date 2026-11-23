using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>描述用于轮廓投影的不可变阴影参数快照。</summary>
public sealed record ShadowEffectSpec(
    Color Color,
    double Opacity,
    double BlurRadius,
    double OffsetX,
    double OffsetY);

/// <summary>为支持阴影的视觉宿主提供可绑定的阴影参数附加属性。</summary>
[Bindable]
public sealed class ShadowEffect : DependencyObject
{
    public static readonly DependencyProperty ShadowProperty = DependencyProperty.RegisterAttached(
        "Shadow",
        typeof(ShadowEffectSpec),
        typeof(ShadowEffect),
        new PropertyMetadata(null));

    public static ShadowEffectSpec? GetShadow(DependencyObject target)
        => (ShadowEffectSpec?)target.GetValue(ShadowProperty);

    public static void SetShadow(DependencyObject target, ShadowEffectSpec? value)
        => target.SetValue(ShadowProperty, value);
}
