using Microsoft.Graphics.Canvas.Effects;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>对后方应用内容应用实时高斯模糊的合成画刷。</summary>
public sealed class BackdropBlurBrush : XamlCompositionBrushBase
{
    public static readonly DependencyProperty BlurAmountProperty = DependencyProperty.Register(
        nameof(BlurAmount),
        typeof(double),
        typeof(BackdropBlurBrush),
        new PropertyMetadata(10.0, OnBlurAmountChanged));

    private CompositionEffectBrush? _effectBrush;

    public double BlurAmount
    {
        get => (double)GetValue(BlurAmountProperty);
        set => SetValue(BlurAmountProperty, value);
    }

    protected override void OnConnected()
    {
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

        using var blurEffect = new GaussianBlurEffect
        {
            Name = "Blur",
            BlurAmount = (float)Math.Max(0, BlurAmount),
            BorderMode = EffectBorderMode.Hard,
            Optimization = EffectOptimization.Balanced,
            Source = new CompositionEffectSourceParameter("backdrop")
        };

        using var factory = compositor.CreateEffectFactory(blurEffect, ["Blur.BlurAmount"]);
        using var backdrop = compositor.CreateBackdropBrush();
        _effectBrush = factory.CreateBrush();
        _effectBrush.SetSourceParameter("backdrop", backdrop);
        CompositionBrush = _effectBrush;
    }

    protected override void OnDisconnected()
    {
        CompositionBrush?.Dispose();
        CompositionBrush = null;
        _effectBrush = null;
    }

    private static void OnBlurAmountChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is BackdropBlurBrush brush && brush._effectBrush is { } effectBrush)
        {
            effectBrush.Properties.InsertScalar("Blur.BlurAmount", (float)Math.Max(0, (double)args.NewValue));
        }
    }
}
