using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Models.Media;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.MediaControls;

public sealed class MediaTextBlock : MediaPartControl, ITextStyleTarget
{
    public static readonly DependencyProperty FieldProperty = DependencyProperty.Register(
        nameof(Field), typeof(MediaTextField), typeof(MediaTextBlock),
        new PropertyMetadata(MediaTextField.Title, (sender, _) => ((MediaTextBlock)sender).UpdateState()));

    private readonly TextBlock _text = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    public FrameworkElement TextElement => _text;
    public MediaTextField Field
    {
        get => (MediaTextField)GetValue(FieldProperty);
        set => SetValue(FieldProperty, value);
    }

    public MediaTextBlock() => Content = _text;
    protected override void UpdateState() => _text.Text = State.GetText(Field, DateTimeOffset.UtcNow);
}
