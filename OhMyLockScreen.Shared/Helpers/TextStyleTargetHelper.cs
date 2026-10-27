using OhMyLockScreen.Shared.Abstractions.Controls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Helpers;

/// <summary>只解析目标自身和组件声明的文本元素，不遍历子元素。</summary>
public static class TextStyleTargetHelper
{
    public sealed record FontProperties(
        DependencyProperty Size,
        DependencyProperty Spacing,
        DependencyProperty Style,
        DependencyProperty Family,
        DependencyProperty Weight);

    public static FontProperties? GetFontProperties(FrameworkElement target) => target switch
    {
        Control => new(Control.FontSizeProperty, Control.CharacterSpacingProperty,
            Control.FontStyleProperty, Control.FontFamilyProperty, Control.FontWeightProperty),
        TextBlock => new(TextBlock.FontSizeProperty, TextBlock.CharacterSpacingProperty,
            TextBlock.FontStyleProperty, TextBlock.FontFamilyProperty, TextBlock.FontWeightProperty),
        RichTextBlock => new(RichTextBlock.FontSizeProperty, RichTextBlock.CharacterSpacingProperty,
            RichTextBlock.FontStyleProperty, RichTextBlock.FontFamilyProperty, RichTextBlock.FontWeightProperty),
        ContentPresenter => new(ContentPresenter.FontSizeProperty, ContentPresenter.CharacterSpacingProperty,
            ContentPresenter.FontStyleProperty, ContentPresenter.FontFamilyProperty, ContentPresenter.FontWeightProperty),
        _ => null
    };

    public static FrameworkElement? ResolveTextElement(FrameworkElement target) => target switch
    {
        ITextStyleTarget component => component.TextElement,
        ContentPresenter { Content: ITextStyleTarget component } => component.TextElement,
        TextBlock or RichTextBlock or ContentPresenter => target,
        _ => null
    };
}
