using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Models.Media;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Controls.Components;

public abstract class MediaSettingsControl<TSettings> : ControlWithSettingsBase<TSettings> where TSettings : MediaSettingsBase
{
    protected override void OnSettingsChanged()
    {
        var panel = new StackPanel { Style = (Style)Application.Current.Resources["EditorFormStyle"] };
        AddOptions(panel);
        var hide = new CheckBox { Content = "没有媒体会话时隐藏" };
        hide.SetBinding(ToggleButtonIsChecked, Bind(nameof(MediaSettingsBase.HideWhenNoMedia)));
        panel.Children.Add(hide);
        Content = panel;
    }

    private static DependencyProperty ToggleButtonIsChecked => Windows.UI.Xaml.Controls.Primitives.ToggleButton.IsCheckedProperty;
    protected virtual void AddOptions(StackPanel panel) { }
    protected Binding Bind(string path) => new() { Source = Settings, Path = new PropertyPath(path), Mode = BindingMode.TwoWay };

    protected void AddChoice<T>(StackPanel panel, string header, string property, params (string Name, T Value)[] options)
    {
        var combo = new ComboBox
        {
            Header = header,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = options.Select(option => new ComboBoxItem { Content = option.Name }).ToArray()
        };
        var binding = Bind(property);
        binding.Converter = new EnumIndexConverter();
        combo.SetBinding(Windows.UI.Xaml.Controls.Primitives.Selector.SelectedIndexProperty, binding);
        panel.Children.Add(combo);
    }
}

public sealed class MediaSettingsControl : MediaSettingsControl<MediaComponentSettings>
{
    protected override void AddOptions(StackPanel panel) => AddChoice(panel, "形态", nameof(Settings.Variant),
        ("仅图标", MediaVariant.IconOnly), ("迷你", MediaVariant.Mini), ("精简", MediaVariant.Lite),
        ("沉浸", MediaVariant.Immersive), ("完整", MediaVariant.Full), ("沉浸完整", MediaVariant.ImmersiveFull));
}

public sealed class MediaTextSettingsControl : MediaSettingsControl<MediaTextComponentSettings>
{
    protected override void AddOptions(StackPanel panel) => AddChoice(panel, "显示信息", nameof(Settings.Field),
        ("标题", MediaTextField.Title), ("艺术家", MediaTextField.Artist), ("副标题", MediaTextField.Subtitle),
        ("专辑", MediaTextField.AlbumTitle), ("专辑艺术家", MediaTextField.AlbumArtist), ("流派", MediaTextField.Genres),
        ("曲目编号", MediaTextField.TrackNumber), ("专辑曲目数", MediaTextField.AlbumTrackCount),
        ("媒体类型", MediaTextField.PlaybackType), ("来源应用", MediaTextField.SourceAppId),
        ("已播放时长", MediaTextField.Position), ("总时长", MediaTextField.Duration), ("剩余时长", MediaTextField.Remaining));
}

public sealed class MediaControllerSettingsControl : MediaSettingsControl<MediaControllerComponentSettings>
{
    protected override void AddOptions(StackPanel panel) => AddChoice(panel, "控制功能", nameof(Settings.Action),
        ("播放/暂停", MediaAction.PlayPause), ("上一首", MediaAction.Previous), ("下一首", MediaAction.Next));
}

public sealed class MediaCoverSettingsControl : MediaSettingsControl<MediaCoverComponentSettings>
{
    protected override void AddOptions(StackPanel panel)
    {
        var dim = new CheckBox { Content = "侧边压暗" };
        dim.SetBinding(Windows.UI.Xaml.Controls.Primitives.ToggleButton.IsCheckedProperty, Bind(nameof(Settings.ShowSideDim)));
        panel.Children.Add(dim);
    }
}

public sealed class MediaProgressSettingsControl : MediaSettingsControl<MediaProgressComponentSettings>;
