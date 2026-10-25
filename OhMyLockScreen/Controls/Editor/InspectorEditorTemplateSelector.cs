using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Controls.Editor;

public sealed class InspectorEditorTemplateSelector : DataTemplateSelector
{
    public DataTemplate? LayerTemplate
    {
        get; set;
    }
    public DataTemplate? EditorTemplate
    {
        get; set;
    }

    protected override DataTemplate SelectTemplateCore(object item) => item is InspectorSection { Model: LayerSettings, Feature: null } ? LayerTemplate! : EditorTemplate!;
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        return SelectTemplateCore(item);
    }
}