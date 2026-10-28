using System.ComponentModel;
using OhMyLockScreen.Shared.Helpers.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ComponentPresenter
{
    private Brush? _foregroundSource;

    /// <summary>内容模板使用的前景；合成画刷的连接由当前内容独占。</summary>
    public Brush? ContentForeground { get; private set; }

    private void OnForegroundChanged(DependencyObject sender, DependencyProperty property) => RefreshContentForeground();

    // Inherited foreground changes during realization do not always notify XAML bindings.
    private void OnForegroundLayoutUpdated(object? sender, object args) => RefreshContentForeground();

    private void RefreshContentForeground(bool force = false)
    {
        if (!_isLoaded || !force && ReferenceEquals(_foregroundSource, Foreground))
        {
            return;
        }

        _foregroundSource = Foreground;
        // Each content subtree owns its composition brush connection. A sibling's unload
        // must not disconnect the brush still used by the other components.
        ContentForeground = _foregroundSource is XamlCompositionBrushBase brush
            && BrushEditorOperations.Kind(brush) != BrushEditorKind.Unsupported
                ? BrushEditorOperations.Copy(brush)
                : _foregroundSource;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContentForeground)));
        // Nested presenters may have observed their inherited value earlier in this pass.
        InvalidateArrange();
    }

    private void ClearContentForeground()
    {
        _foregroundSource = null;
        ContentForeground = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContentForeground)));
    }
}
