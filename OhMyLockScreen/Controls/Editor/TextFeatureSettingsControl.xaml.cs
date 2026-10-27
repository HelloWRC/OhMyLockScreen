using Windows.UI.Text;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Controls.Editor;

public sealed partial class TextFeatureSettingsControl : TextFeatureSettingsControlBase
{
    public IReadOnlyList<EditorChoice<TextAlignment>> Alignments { get; } =
    [
        new("左对齐", TextAlignment.Left),
        new("居中", TextAlignment.Center),
        new("右对齐", TextAlignment.Right),
        new("两端对齐", TextAlignment.Justify)
    ];

    public IReadOnlyList<EditorChoice<FontStyle>> FontStyles { get; } =
    [
        new("常规", FontStyle.Normal),
        new("斜体", FontStyle.Italic),
        new("倾斜", FontStyle.Oblique)
    ];

    public TextFeatureSettingsControl() => InitializeComponent();

    protected override void OnSettingsChanged() => Bindings.Update();
}
