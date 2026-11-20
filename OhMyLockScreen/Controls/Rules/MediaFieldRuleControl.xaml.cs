using OhMyLockScreen.Shared.Models.Media;

namespace OhMyLockScreen.Controls.Rules;

public sealed partial class MediaFieldRuleControl : MediaFieldRuleControlBase
{
    private readonly (string Name, MediaTextField Value)[] _fields = MediaTextFields.Options;
    public IReadOnlyList<string> FieldNames { get; }

    public MediaFieldRuleControl()
    {
        FieldNames = _fields.Select(field => field.Name).ToArray();
        InitializeComponent();
    }

    private int GetFieldIndex(MediaTextField value) => Array.FindIndex(_fields, field => field.Value == value);

    private void SetFieldIndex(int index)
    {
        if ((uint)index < _fields.Length) Settings.Field = _fields[index].Value;
    }

    protected override void OnSettingsChanged() => Bindings.Update();
}
