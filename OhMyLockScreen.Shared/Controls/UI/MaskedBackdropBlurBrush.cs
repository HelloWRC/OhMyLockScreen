using System.Numerics;
using System.Reactive.Disposables;
using System.Runtime.Versioning;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>对后方应用内容应用自上而下渐进的实时高斯模糊与深色遮罩的合成画刷。</summary>
public sealed class MaskedBackdropBlurBrush : XamlCompositionBrushBase
{
    public static readonly DependencyProperty BlurAmountProperty = DependencyProperty.Register(
        nameof(BlurAmount),
        typeof(double),
        typeof(MaskedBackdropBlurBrush),
        new PropertyMetadata(16.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty TintColorProperty = DependencyProperty.Register(
        nameof(TintColor),
        typeof(Color),
        typeof(MaskedBackdropBlurBrush),
        new PropertyMetadata(Color.FromArgb(0xA3, 0, 0, 0), OnVisualPropertyChanged));

    public static readonly DependencyProperty MaskFadeHeightProperty = DependencyProperty.Register(
        nameof(MaskFadeHeight),
        typeof(double),
        typeof(MaskedBackdropBlurBrush),
        new PropertyMetadata(0.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty MaskOffsetYProperty = DependencyProperty.Register(
        nameof(MaskOffsetY),
        typeof(double),
        typeof(MaskedBackdropBlurBrush),
        new PropertyMetadata(0.0, OnVisualPropertyChanged));

    private CompositeDisposable? _resources;
    private CompositionEffectBrush? _effectBrush;
    private CompositionLinearGradientBrush? _maskBrush;
    private CompositionColorGradientStop[]? _tintStops;
    private readonly List<(Vector2 Cp1, Vector2 Cp2, CubicBezierEasingFunction Easing)> _maskEasings = [];
    private bool _isTintOnlyGradient;
    private float _targetMaskOffsetY;
    private bool _hasExplicitTargetOffset;

    public double BlurAmount
    {
        get => (double)GetValue(BlurAmountProperty)!;
        set => SetValue(BlurAmountProperty, value);
    }

    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty)!;
        set => SetValue(TintColorProperty, value);
    }

    public double MaskFadeHeight
    {
        get => (double)GetValue(MaskFadeHeightProperty)!;
        set => SetValue(MaskFadeHeightProperty, value);
    }

    public double MaskOffsetY
    {
        get => (double)GetValue(MaskOffsetYProperty)!;
        set => SetValue(MaskOffsetYProperty, value);
    }

    public bool SupportsAnimatedMask => _maskBrush != null;

    public void SetMaskOffsetYImmediate(float targetY)
    {
        _hasExplicitTargetOffset = true;
        _targetMaskOffsetY = targetY;
        if (_maskBrush == null)
        {
            return;
        }

        _maskBrush.StopAnimation("Offset");
        _maskBrush.Offset = new Vector2(0f, targetY);
    }

    public void AnimateMaskOffsetY(float targetY, TimeSpan duration, Vector2 controlPoint1, Vector2 controlPoint2)
    {
        _hasExplicitTargetOffset = true;
        _targetMaskOffsetY = targetY;
        if (_maskBrush == null)
        {
            return;
        }

        if (duration <= TimeSpan.Zero)
        {
            SetMaskOffsetYImmediate(targetY);
            return;
        }

        var compositor = _maskBrush.Compositor;
        CubicBezierEasingFunction? easing = null;
        for (var index = 0; index < _maskEasings.Count; index++)
        {
            if (_maskEasings[index].Cp1 == controlPoint1 && _maskEasings[index].Cp2 == controlPoint2)
            {
                easing = _maskEasings[index].Easing;
                break;
            }
        }

        if (easing == null)
        {
            easing = compositor.CreateCubicBezierEasingFunction(controlPoint1, controlPoint2);
            _maskEasings.Add((controlPoint1, controlPoint2, easing));
        }

        var animation = compositor.CreateVector2KeyFrameAnimation();
        animation.Duration = duration;
        // 省略 0.0f 关键帧，使悬停态中途反转时可直接从当前实时值平滑衔接。
        animation.InsertKeyFrame(1.0f, new Vector2(0f, targetY), easing);
        _maskBrush.StartAnimation("Offset", animation);
    }

    protected override void OnConnected()
    {
        FallbackColor = TintColor;
        if (CompositionBrush != null)
        {
            return;
        }

        var compositor = Window.Current?.Compositor;
        if (compositor == null)
        {
            return;
        }

        var fadeHeight = MaskFadeHeight;
        var initialOffsetY = _hasExplicitTargetOffset ? _targetMaskOffsetY : (float)MaskOffsetY;
        var resources = new CompositeDisposable();
        CompositionEffectBrush? effectBrush = null;
        CompositionLinearGradientBrush? maskBrush = null;

        try
        {
            if (!CompositionCapabilities.GetForCurrentView().AreEffectsSupported())
            {
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
                {
                    var tintGradient = CreateMaskGradient(
                        compositor,
                        resources,
                        fadeHeight,
                        initialOffsetY,
                        TintColor,
                        includeBrushInResources: false,
                        out var tintStops);

                    _resources = resources;
                    _maskBrush = tintGradient;
                    _tintStops = tintStops;
                    _isTintOnlyGradient = true;
                    CompositionBrush = tintGradient;
                }
                else
                {
                    resources.Dispose();
                }

                return;
            }

            using var blurEffect = new GaussianBlurEffect
            {
                Name = "Blur",
                BlurAmount = (float)Math.Max(0, BlurAmount),
                BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Balanced,
                Source = new CompositionEffectSourceParameter("backdrop")
            };

            using var tintEffect = new ColorSourceEffect
            {
                Name = "Tint",
                Color = TintColor
            };

            using var compositeEffect = new CompositeEffect
            {
                Mode = CanvasComposite.SourceOver,
                Sources = { blurEffect, tintEffect }
            };

            var backdrop = compositor.CreateBackdropBrush();
            resources.Add(backdrop);

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
            {
                using var alphaMaskEffect = new AlphaMaskEffect
                {
                    Source = compositeEffect,
                    AlphaMask = new CompositionEffectSourceParameter("mask")
                };

                using var factory = compositor.CreateEffectFactory(alphaMaskEffect, (string[])["Blur.BlurAmount", "Tint.Color"]);
                maskBrush = CreateMaskGradient(
                    compositor,
                    resources,
                    fadeHeight,
                    initialOffsetY,
                    Colors.White,
                    includeBrushInResources: true,
                    out _);

                effectBrush = factory.CreateBrush();
                effectBrush.SetSourceParameter("backdrop", backdrop);
                effectBrush.SetSourceParameter("mask", maskBrush);
            }
            else
            {
                using var factory = compositor.CreateEffectFactory(compositeEffect, (string[])["Blur.BlurAmount", "Tint.Color"]);
                effectBrush = factory.CreateBrush();
                effectBrush.SetSourceParameter("backdrop", backdrop);
            }

            _resources = resources;
            _effectBrush = effectBrush;
            _maskBrush = maskBrush;
            _tintStops = null;
            _isTintOnlyGradient = false;
            CompositionBrush = effectBrush;
        }
        catch
        {
            effectBrush?.Dispose();
            if (maskBrush != null && !resources.Contains(maskBrush))
            {
                maskBrush.Dispose();
            }

            resources.Dispose();
            throw;
        }
    }

    protected override void OnDisconnected()
    {
        CompositionBrush?.Dispose();
        CompositionBrush = null;
        _effectBrush = null;
        _maskBrush = null;
        _tintStops = null;
        _isTintOnlyGradient = false;
        foreach (var (_, _, easing) in _maskEasings)
        {
            easing.Dispose();
        }
        _maskEasings.Clear();
        _resources?.Dispose();
        _resources = null;
    }

    [SupportedOSPlatform("windows10.0.18362.0")]
    private static CompositionLinearGradientBrush CreateMaskGradient(
        Compositor compositor,
        CompositeDisposable resources,
        double fadeHeight,
        float initialOffsetY,
        Color baseColor,
        bool includeBrushInResources,
        out CompositionColorGradientStop[] stops)
    {
        var gradient = compositor.CreateLinearGradientBrush();
        if (includeBrushInResources)
        {
            resources.Add(gradient);
        }

        if (fadeHeight > 0)
        {
            gradient.MappingMode = CompositionMappingMode.Absolute;
            gradient.StartPoint = Vector2.Zero;
            gradient.EndPoint = new Vector2(0f, (float)fadeHeight);
            gradient.ExtendMode = CompositionGradientExtendMode.Clamp;
            gradient.Offset = new Vector2(0f, initialOffsetY);
        }
        else
        {
            gradient.MappingMode = CompositionMappingMode.Relative;
            gradient.StartPoint = Vector2.Zero;
            gradient.EndPoint = new Vector2(0f, 1f);
        }

        var spec = GetGradientStopsSpec(fadeHeight, baseColor);
        stops = new CompositionColorGradientStop[spec.Length];
        for (var index = 0; index < spec.Length; index++)
        {
            var stop = compositor.CreateColorGradientStop(spec[index].Offset, spec[index].Color);
            resources.Add(stop);
            gradient.ColorStops.Add(stop);
            stops[index] = stop;
        }

        return gradient;
    }

    private static (float Offset, Color Color)[] GetGradientStopsSpec(double fadeHeight, Color baseColor)
    {
        if (fadeHeight > 0)
        {
            return
            [
                (0f, ScaleAlpha(baseColor, 0f)),
                (0.25f, ScaleAlpha(baseColor, 0.4f)),
                (1f, ScaleAlpha(baseColor, 1f))
            ];
        }

        return
        [
            (0f, ScaleAlpha(baseColor, 0f)),
            (0.32f, ScaleAlpha(baseColor, 0.7529412f)),
            (1f, ScaleAlpha(baseColor, 1f))
        ];
    }

    private static Color ScaleAlpha(Color color, float factor) =>
        Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(factor, 0f, 1f)), color.R, color.G, color.B);

    private static void OnVisualPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not MaskedBackdropBlurBrush brush)
        {
            return;
        }

        if (args.Property == TintColorProperty)
        {
            var color = (Color)args.NewValue!;
            brush.FallbackColor = color;
            brush._effectBrush?.Properties.InsertColor("Tint.Color", color);
            if (brush._isTintOnlyGradient && brush._tintStops is { Length: > 0 } stops)
            {
                var spec = GetGradientStopsSpec(brush.MaskFadeHeight, color);
                for (var index = 0; index < stops.Length && index < spec.Length; index++)
                {
                    stops[index].Color = spec[index].Color;
                }
            }
        }
        else if (args.Property == BlurAmountProperty)
        {
            brush._effectBrush?.Properties.InsertScalar("Blur.BlurAmount", (float)Math.Max(0, (double)args.NewValue!));
        }
        else if (args.Property == MaskFadeHeightProperty)
        {
            var fadeHeight = (double)args.NewValue!;
            if (brush._maskBrush != null && fadeHeight > 0)
            {
                brush._maskBrush.MappingMode = CompositionMappingMode.Absolute;
                brush._maskBrush.EndPoint = new Vector2(0f, (float)fadeHeight);
                brush._maskBrush.ExtendMode = CompositionGradientExtendMode.Clamp;
            }
        }
        else if (args.Property == MaskOffsetYProperty)
        {
            var offsetY = (float)(double)args.NewValue!;
            brush._hasExplicitTargetOffset = false;
            brush._targetMaskOffsetY = offsetY;
            if (brush._maskBrush != null)
            {
                brush._maskBrush.StopAnimation("Offset");
                brush._maskBrush.Offset = new Vector2(0f, offsetY);
            }
        }
    }
}
