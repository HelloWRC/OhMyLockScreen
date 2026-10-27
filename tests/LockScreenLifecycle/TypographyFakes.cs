// Portable stand-ins. Native inheritance and text rendering are checked in the UWP preview.
namespace Windows.UI.Text
{
    public enum FontStyle { Normal, Oblique, Italic }
    [Flags]
    public enum TextDecorations { None = 0, Underline = 1, Strikethrough = 2 }
    public struct FontWeight
    {
        public ushort Weight { get; set; }
    }
}

namespace Windows.UI.Xaml
{
    public enum TextAlignment { Center, Left, Right, Justify, DetectFromContent }
    public enum LineStackingStrategy { MaxHeight, BlockLineHeight, BaselineToBaseline }
}

namespace Windows.UI.Xaml.Controls
{
    public partial class Control
    {
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
            nameof(FontSize), typeof(double), typeof(Control), new PropertyMetadata(14d));

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty)!;
            set => SetValue(FontSizeProperty, value);
        }

        public static readonly DependencyProperty CharacterSpacingProperty = DependencyProperty.Register(
            nameof(CharacterSpacing), typeof(int), typeof(Control), new PropertyMetadata(0));

        public int CharacterSpacing
        {
            get => (int)GetValue(CharacterSpacingProperty)!;
            set => SetValue(CharacterSpacingProperty, value);
        }

        public static readonly DependencyProperty FontStyleProperty = DependencyProperty.Register(
            nameof(FontStyle), typeof(Windows.UI.Text.FontStyle), typeof(Control), new PropertyMetadata(Windows.UI.Text.FontStyle.Normal));

        public Windows.UI.Text.FontStyle FontStyle
        {
            get => (Windows.UI.Text.FontStyle)GetValue(FontStyleProperty)!;
            set => SetValue(FontStyleProperty, value);
        }

        public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
            nameof(FontFamily), typeof(Media.FontFamily), typeof(Control), new PropertyMetadata(new Media.FontFamily("Default")));

        public Media.FontFamily FontFamily
        {
            get => (Media.FontFamily)GetValue(FontFamilyProperty)!;
            set => SetValue(FontFamilyProperty, value);
        }

        public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(
            nameof(FontWeight), typeof(Windows.UI.Text.FontWeight), typeof(Control), new PropertyMetadata(new Windows.UI.Text.FontWeight { Weight = 400 }));

        public Windows.UI.Text.FontWeight FontWeight
        {
            get => (Windows.UI.Text.FontWeight)GetValue(FontWeightProperty)!;
            set => SetValue(FontWeightProperty, value);
        }

    }

    public partial class ContentPresenter
    {
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
            nameof(FontSize), typeof(double), typeof(ContentPresenter), new PropertyMetadata(14d));

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty)!;
            set => SetValue(FontSizeProperty, value);
        }

        public static readonly DependencyProperty CharacterSpacingProperty = DependencyProperty.Register(
            nameof(CharacterSpacing), typeof(int), typeof(ContentPresenter), new PropertyMetadata(0));

        public int CharacterSpacing
        {
            get => (int)GetValue(CharacterSpacingProperty)!;
            set => SetValue(CharacterSpacingProperty, value);
        }

        public static readonly DependencyProperty FontStyleProperty = DependencyProperty.Register(
            nameof(FontStyle), typeof(Windows.UI.Text.FontStyle), typeof(ContentPresenter), new PropertyMetadata(Windows.UI.Text.FontStyle.Normal));

        public Windows.UI.Text.FontStyle FontStyle
        {
            get => (Windows.UI.Text.FontStyle)GetValue(FontStyleProperty)!;
            set => SetValue(FontStyleProperty, value);
        }

        public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
            nameof(FontFamily), typeof(Media.FontFamily), typeof(ContentPresenter), new PropertyMetadata(new Media.FontFamily("Default")));

        public Media.FontFamily FontFamily
        {
            get => (Media.FontFamily)GetValue(FontFamilyProperty)!;
            set => SetValue(FontFamilyProperty, value);
        }

        public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(
            nameof(FontWeight), typeof(Windows.UI.Text.FontWeight), typeof(ContentPresenter), new PropertyMetadata(new Windows.UI.Text.FontWeight { Weight = 400 }));

        public Windows.UI.Text.FontWeight FontWeight
        {
            get => (Windows.UI.Text.FontWeight)GetValue(FontWeightProperty)!;
            set => SetValue(FontWeightProperty, value);
        }

        public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(
            nameof(LineHeight), typeof(double), typeof(ContentPresenter), new PropertyMetadata(0d));

        public double LineHeight
        {
            get => (double)GetValue(LineHeightProperty)!;
            set => SetValue(LineHeightProperty, value);
        }

        public static readonly DependencyProperty LineStackingStrategyProperty = DependencyProperty.Register(
            nameof(LineStackingStrategy), typeof(LineStackingStrategy), typeof(ContentPresenter), new PropertyMetadata(LineStackingStrategy.MaxHeight));

        public LineStackingStrategy LineStackingStrategy
        {
            get => (LineStackingStrategy)GetValue(LineStackingStrategyProperty)!;
            set => SetValue(LineStackingStrategyProperty, value);
        }

    }

    public sealed partial class TextBlock
    {
        public static readonly DependencyProperty CharacterSpacingProperty = DependencyProperty.Register(
            nameof(CharacterSpacing), typeof(int), typeof(TextBlock), new PropertyMetadata(0));

        public int CharacterSpacing
        {
            get => (int)GetValue(CharacterSpacingProperty)!;
            set => SetValue(CharacterSpacingProperty, value);
        }

        public static readonly DependencyProperty FontStyleProperty = DependencyProperty.Register(
            nameof(FontStyle), typeof(Windows.UI.Text.FontStyle), typeof(TextBlock), new PropertyMetadata(Windows.UI.Text.FontStyle.Normal));

        public Windows.UI.Text.FontStyle FontStyle
        {
            get => (Windows.UI.Text.FontStyle)GetValue(FontStyleProperty)!;
            set => SetValue(FontStyleProperty, value);
        }

        public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
            nameof(FontFamily), typeof(Media.FontFamily), typeof(TextBlock), new PropertyMetadata(new Media.FontFamily("Default")));

        public Media.FontFamily FontFamily
        {
            get => (Media.FontFamily)GetValue(FontFamilyProperty)!;
            set => SetValue(FontFamilyProperty, value);
        }

        public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(
            nameof(FontWeight), typeof(Windows.UI.Text.FontWeight), typeof(TextBlock), new PropertyMetadata(new Windows.UI.Text.FontWeight { Weight = 400 }));

        public Windows.UI.Text.FontWeight FontWeight
        {
            get => (Windows.UI.Text.FontWeight)GetValue(FontWeightProperty)!;
            set => SetValue(FontWeightProperty, value);
        }

        public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(
            nameof(LineHeight), typeof(double), typeof(TextBlock), new PropertyMetadata(0d));

        public double LineHeight
        {
            get => (double)GetValue(LineHeightProperty)!;
            set => SetValue(LineHeightProperty, value);
        }

        public static readonly DependencyProperty LineStackingStrategyProperty = DependencyProperty.Register(
            nameof(LineStackingStrategy), typeof(LineStackingStrategy), typeof(TextBlock), new PropertyMetadata(LineStackingStrategy.MaxHeight));

        public LineStackingStrategy LineStackingStrategy
        {
            get => (LineStackingStrategy)GetValue(LineStackingStrategyProperty)!;
            set => SetValue(LineStackingStrategyProperty, value);
        }

        public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
            nameof(TextAlignment), typeof(TextAlignment), typeof(TextBlock), new PropertyMetadata(TextAlignment.Left));

        public TextAlignment TextAlignment
        {
            get => (TextAlignment)GetValue(TextAlignmentProperty)!;
            set => SetValue(TextAlignmentProperty, value);
        }

        public static readonly DependencyProperty TextDecorationsProperty = DependencyProperty.Register(
            nameof(TextDecorations), typeof(Windows.UI.Text.TextDecorations), typeof(TextBlock), new PropertyMetadata(Windows.UI.Text.TextDecorations.None));

        public Windows.UI.Text.TextDecorations TextDecorations
        {
            get => (Windows.UI.Text.TextDecorations)GetValue(TextDecorationsProperty)!;
            set => SetValue(TextDecorationsProperty, value);
        }

    }

    public sealed partial class RichTextBlock
    {
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
            nameof(FontSize), typeof(double), typeof(RichTextBlock), new PropertyMetadata(14d));

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty)!;
            set => SetValue(FontSizeProperty, value);
        }

        public static readonly DependencyProperty CharacterSpacingProperty = DependencyProperty.Register(
            nameof(CharacterSpacing), typeof(int), typeof(RichTextBlock), new PropertyMetadata(0));

        public int CharacterSpacing
        {
            get => (int)GetValue(CharacterSpacingProperty)!;
            set => SetValue(CharacterSpacingProperty, value);
        }

        public static readonly DependencyProperty FontStyleProperty = DependencyProperty.Register(
            nameof(FontStyle), typeof(Windows.UI.Text.FontStyle), typeof(RichTextBlock), new PropertyMetadata(Windows.UI.Text.FontStyle.Normal));

        public Windows.UI.Text.FontStyle FontStyle
        {
            get => (Windows.UI.Text.FontStyle)GetValue(FontStyleProperty)!;
            set => SetValue(FontStyleProperty, value);
        }

        public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
            nameof(FontFamily), typeof(Media.FontFamily), typeof(RichTextBlock), new PropertyMetadata(new Media.FontFamily("Default")));

        public Media.FontFamily FontFamily
        {
            get => (Media.FontFamily)GetValue(FontFamilyProperty)!;
            set => SetValue(FontFamilyProperty, value);
        }

        public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(
            nameof(FontWeight), typeof(Windows.UI.Text.FontWeight), typeof(RichTextBlock), new PropertyMetadata(new Windows.UI.Text.FontWeight { Weight = 400 }));

        public Windows.UI.Text.FontWeight FontWeight
        {
            get => (Windows.UI.Text.FontWeight)GetValue(FontWeightProperty)!;
            set => SetValue(FontWeightProperty, value);
        }

        public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(
            nameof(LineHeight), typeof(double), typeof(RichTextBlock), new PropertyMetadata(0d));

        public double LineHeight
        {
            get => (double)GetValue(LineHeightProperty)!;
            set => SetValue(LineHeightProperty, value);
        }

        public static readonly DependencyProperty LineStackingStrategyProperty = DependencyProperty.Register(
            nameof(LineStackingStrategy), typeof(LineStackingStrategy), typeof(RichTextBlock), new PropertyMetadata(LineStackingStrategy.MaxHeight));

        public LineStackingStrategy LineStackingStrategy
        {
            get => (LineStackingStrategy)GetValue(LineStackingStrategyProperty)!;
            set => SetValue(LineStackingStrategyProperty, value);
        }

        public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
            nameof(TextAlignment), typeof(TextAlignment), typeof(RichTextBlock), new PropertyMetadata(TextAlignment.Left));

        public TextAlignment TextAlignment
        {
            get => (TextAlignment)GetValue(TextAlignmentProperty)!;
            set => SetValue(TextAlignmentProperty, value);
        }

    }

}

namespace Microsoft.Graphics.Canvas.Text
{
    public static class CanvasTextFormat
    {
        public static string[] GetSystemFontFamilies() => ["Segoe UI", "Arial"];
    }
}

