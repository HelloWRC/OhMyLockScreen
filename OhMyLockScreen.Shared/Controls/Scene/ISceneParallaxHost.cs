using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Shared.Controls.Scene;

/// <summary>为场景内的图层提供与背景同步的视差驱动。</summary>
public interface ISceneParallaxHost
{
    /// <summary>注册一个受场景视差驱动的变换；strengthOverride 为 null 时跟随背景视差强度。</summary>
    ISceneParallaxRegistration Register(CompositeTransform transform, double? strengthOverride);
}

/// <summary>已注册图层视差的句柄；释放时从场景移除并重置变换。</summary>
public interface ISceneParallaxRegistration : IDisposable
{
    /// <summary>更新视差强度覆盖；传入 null 时恢复跟随背景视差强度。</summary>
    void SetStrengthOverride(double? strengthOverride);
}
