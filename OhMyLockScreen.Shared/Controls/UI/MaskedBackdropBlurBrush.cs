using System.Numerics;
using System.Reactive.Disposables;
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

    private CompositeDisposable? _resources;
    private CompositionEffectBrush? _effectBrush;

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

    protected override void OnConnected()
    {
        FallbackColor = TintColor;
        if (CompositionBrush != null)
        {
            return;
        }

        if (!CompositionCapabilities.GetForCurrentView().AreEffectsSupported())
        {
            return;
        }

        var compositor = Window.Current?.Compositor;
        if (compositor == null)
        {
            return;
        }

        var resources = new CompositeDisposable();
        CompositionEffectBrush? effectBrush = null;
        try
        {
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
                var gradient = compositor.CreateLinearGradientBrush();
                resources.Add(gradient);
                gradient.MappingMode = CompositionMappingMode.Relative;
                gradient.StartPoint = new Vector2(0f, 0f);
                gradient.EndPoint = new Vector2(0f, 1f);

                var stopTop = compositor.CreateColorGradientStop(0f, Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
                var stopMid = compositor.CreateColorGradientStop(0.32f, Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF));
                var stopBottom = compositor.CreateColorGradientStop(1f, Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
                resources.Add(stopTop);
                resources.Add(stopMid);
                resources.Add(stopBottom);
                gradient.ColorStops.Add(stopTop);
                gradient.ColorStops.Add(stopMid);
                gradient.ColorStops.Add(stopBottom);

                effectBrush = factory.CreateBrush();
                effectBrush.SetSourceParameter("backdrop", backdrop);
                effectBrush.SetSourceParameter("mask", gradient);
            }
            else
            {
                using var factory = compositor.CreateEffectFactory(compositeEffect, (string[])["Blur.BlurAmount", "Tint.Color"]);
                effectBrush = factory.CreateBrush();
                effectBrush.SetSourceParameter("backdrop", backdrop);
            }

            _resources = resources;
            _effectBrush = effectBrush;
            CompositionBrush = effectBrush;
        }
        catch
        {
            effectBrush?.Dispose();
            resources.Dispose();
            throw;
        }
    }

    protected override void OnDisconnected()
    {
        CompositionBrush?.Dispose();
        CompositionBrush = null;
        _effectBrush = null;
        _resources?.Dispose();
        _resources = null;
    }

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
        }
        else if (args.Property == BlurAmountProperty)
        {
            brush._effectBrush?.Properties.InsertScalar("Blur.BlurAmount", (float)Math.Max(0, (double)args.NewValue!));
        }
    }
}
