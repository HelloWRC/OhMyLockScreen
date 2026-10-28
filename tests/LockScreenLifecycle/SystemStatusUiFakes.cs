using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Models.SystemStatus;

namespace OhMyLockScreen.Controls.Components
{
    public sealed class SystemStatusSettingsControl : ControlWithSettingsBase<SystemStatusComponentSettings>;
}

namespace OhMyLockScreen.Services.SystemStatus
{
    internal sealed class AudioVolumeObserver : IDisposable
    {
        public AudioVolumeObserver(Action<SystemStatusSnapshot> publish, Action<Exception> failure) { }
        public void Dispose() { }
    }
}

namespace FluentIcons.Uwp
{
    public sealed class FluentIcon : Windows.UI.Xaml.Controls.FontIcon
    {
        public FluentIcons.Common.Icon Icon { get; set; }
        public FluentIcons.Common.IconSize IconSize { get; set; }
    }
}

namespace Windows.UI.Xaml
{
    public enum TextTrimming { None, CharacterEllipsis, WordEllipsis, Clip }
}

namespace Windows.UI.Xaml.Controls
{
    public class FontIcon : Control;

    public sealed partial class TextBlock
    {
        public Windows.UI.Xaml.TextTrimming TextTrimming { get; set; }
    }
}

namespace Windows.UI.Xaml.Automation
{
    public static class AutomationProperties
    {
        public static void SetName(Windows.UI.Xaml.DependencyObject target, string name) { }
    }
}
