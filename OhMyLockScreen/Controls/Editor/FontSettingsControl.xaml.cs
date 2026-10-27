using System.Collections.ObjectModel;
using OhMyLockScreen.Services;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Controls.Editor;

public sealed class FontFamilyChoice(string name)
{
    public string Name { get; } = name;
    public string DisplayName => string.IsNullOrEmpty(Name) ? "继承" : Name;
    public FontFamily Family { get; } = string.IsNullOrEmpty(name) ? FontFamily.XamlAutoFontFamily : new FontFamily(name);
}

public sealed partial class FontSettingsControl : FontSettingsControlBase
{
    public ObservableCollection<FontFamilyChoice> FontFamilies { get; } = [new("")];

    public IReadOnlyList<EditorChoice<ushort>> FontWeights { get; } =
    [
        new("极细 (100)", 100),
        new("特细 (200)", 200),
        new("细体 (300)", 300),
        new("半细 (350)", 350),
        new("常规 (400)", 400),
        new("中等 (500)", 500),
        new("半粗 (600)", 600),
        new("粗体 (700)", 700),
        new("特粗 (800)", 800),
        new("黑体 (900)", 900),
        new("特黑 (950)", 950)
    ];

    public FontSettingsControl(FontCatalogService fonts)
    {
        foreach (var name in fonts.FontFamilies)
        {
            FontFamilies.Add(new FontFamilyChoice(name));
        }

        InitializeComponent();
    }

    protected override void OnSettingsChanged()
    {
        var name = Settings.FontFamilyName;
        if (!string.IsNullOrEmpty(name) && FontFamilies.All(choice => choice.Name != name))
        {
            FontFamilies.Add(new FontFamilyChoice(name));
        }

        Bindings.Update();
    }
}
