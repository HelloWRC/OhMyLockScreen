using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Controls.MediaControls;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Controls.Components;

[ComponentInfo(Id, "媒体文本", FluentIcons.Common.Icon.Text, "显示所选媒体元数据或播放时间。")]
public sealed class MediaTextComponent : MediaComponentBase<MediaTextComponentSettings>, ITextStyleTarget
{
    public const string Id = "omls.components.mediaText";
    private readonly MediaTextBlock _text = new();
    public FrameworkElement TextElement => _text.TextElement;

    public MediaTextComponent(IMediaStateService service) : base(service) => Content = _text;

    protected override void UpdateMedia()
    {
        _text.Field = Settings.Field;
        _text.State = State;
    }
}

[ComponentInfo(Id, "媒体控制按钮", FluentIcons.Common.Icon.Play, "上一首、播放/暂停或下一首。")]
public sealed class MediaControllerComponent : MediaComponentBase<MediaControllerComponentSettings>
{
    public const string Id = "omls.components.mediaController";
    private readonly MediaController _controller = new();

    public MediaControllerComponent(IMediaStateService service) : base(service)
    {
        _controller.Service = service;
        Content = _controller;
    }

    protected override void UpdateMedia()
    {
        _controller.Action = Settings.Action;
        _controller.IsPlaceholder = IsPlaceholder;
        _controller.State = State;
    }
}

[ComponentInfo(Id, "媒体封面", FluentIcons.Common.Icon.Image, "自适应展示媒体封面与主题色边缘。")]
public sealed class MediaCoverComponent : MediaComponentBase<MediaCoverComponentSettings>
{
    public const string Id = "omls.components.mediaCover";
    private readonly CoverImageControl _cover = new();

    public MediaCoverComponent(IMediaStateService service) : base(service) => Content = _cover;

    protected override void UpdateMedia()
    {
        _cover.ShowSideDim = Settings.ShowSideDim;
        _cover.IsPlaceholder = IsPlaceholder;
        _cover.State = State;
    }
}

[ComponentInfo(Id, "媒体进度条", FluentIcons.Common.Icon.Play, "显示播放进度并跳转到指定位置。")]
public sealed class MediaProgressComponent : MediaComponentBase<MediaProgressComponentSettings>
{
    public const string Id = "omls.components.mediaProgress";
    private readonly MediaProgressBar _progress = new();

    public MediaProgressComponent(IMediaStateService service) : base(service)
    {
        _progress.Service = service;
        Content = _progress;
    }

    protected override void UpdateMedia()
    {
        _progress.IsPlaceholder = IsPlaceholder;
        _progress.State = State;
    }
}
