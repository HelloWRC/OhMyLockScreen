using System.Reactive.Disposables;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Helpers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Services.Features.Appearance;

[FeatureInfo(Id, "文本")]
public sealed class TextFeature : FeatureBase<FrameworkElement, TextFeatureSettings>
{
    public const string Id = "omls.features.appearance.text";
    private CompositeDisposable? _bindings;

    public override void Attach(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Detach();
        Target = target;
        var bindings = _bindings = new CompositeDisposable();

        try
        {
            if (TextStyleTargetHelper.GetFontProperties(target) is { } properties)
            {
                bindings.Add(DependencyPropertyBindingHelper.Bind(Settings, source => source.FontSize, target, properties.Size));
                bindings.Add(DependencyPropertyBindingHelper.Bind(Settings, source => source.CharacterSpacing, target, properties.Spacing));
                bindings.Add(DependencyPropertyBindingHelper.Bind(Settings, source => source.FontStyle, target, properties.Style));
            }

            var text = TextStyleTargetHelper.ResolveTextElement(target);
            var (height, strategy, alignment, decorations) = text switch
            {
                TextBlock => (TextBlock.LineHeightProperty, TextBlock.LineStackingStrategyProperty,
                    TextBlock.TextAlignmentProperty, TextBlock.TextDecorationsProperty),
                RichTextBlock => (RichTextBlock.LineHeightProperty, RichTextBlock.LineStackingStrategyProperty,
                    RichTextBlock.TextAlignmentProperty, null),
                ContentPresenter => (ContentPresenter.LineHeightProperty, ContentPresenter.LineStackingStrategyProperty,
                    null, null),
                _ => ((DependencyProperty?)null, null, null, null)
            };

            if (text != null && height != null && strategy != null)
            {
                bindings.Add(DependencyPropertyBindingHelper.Bind(Settings, source => source.LineHeight, text, height));
                bindings.Add(DependencyPropertyBindingHelper.Bind(Settings, source => source.LineHeight, text, strategy,
                    value => value == 0 ? LineStackingStrategy.MaxHeight : LineStackingStrategy.BlockLineHeight));
            }

            if (text != null && alignment != null)
            {
                bindings.Add(DependencyPropertyBindingHelper.Bind(Settings, source => source.TextAlignment, text, alignment));
            }

            if (text != null && decorations != null)
            {
                bindings.Add(DependencyPropertyBindingHelper.Bind(Settings, source => source.Decorations, text, decorations));
            }
        }
        catch
        {
            Detach();
            throw;
        }
    }

    public override void Detach()
    {
        _bindings?.Dispose();
        _bindings = null;
        Target = null;
    }

    protected override void OnSettingsChanged()
    {
        if (Target is { } target)
        {
            Attach(target);
        }
    }
}
