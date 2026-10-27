using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Attributes.Serialization;
using Windows.UI.Text;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Models.Features.Appearance;

[AutoJsonTypeName("omls.features.appearance.text")]
public partial class TextFeatureSettings : ObservableObject, IFeatureSettings
{
    [ObservableProperty]
    private double _fontSize = 32;

    [ObservableProperty]
    private double _lineHeight;

    [ObservableProperty]
    private int _characterSpacing;

    [ObservableProperty]
    private TextAlignment _textAlignment = TextAlignment.Left;

    [ObservableProperty]
    private FontStyle _fontStyle = FontStyle.Normal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Decorations))]
    private bool _underline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Decorations))]
    private bool _strikethrough;

    [JsonIgnore]
    public TextDecorations Decorations =>
        (Underline ? TextDecorations.Underline : TextDecorations.None)
        | (Strikethrough ? TextDecorations.Strikethrough : TextDecorations.None);
}
