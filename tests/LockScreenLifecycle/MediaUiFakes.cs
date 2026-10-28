// Native media controls and Windows SMTC access are checked in the isolated UWP preview.
// Simulations retain real service, settings, dispatcher, component lifetime and visibility logic.
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Models.Media;

namespace OhMyLockScreen.Controls.Components
{
    [ComponentInfo(Id, "媒体")]
    public sealed class MediaComponent(IMediaStateService service) : MediaComponentBase<MediaComponentSettings>(service)
    {
        public const string Id = "omls.components.media";
        protected override void UpdateMedia() { }
    }

    [ComponentInfo(Id, "媒体文本")]
    public sealed class MediaTextComponent(IMediaStateService service) : MediaComponentBase<MediaTextComponentSettings>(service)
    {
        public const string Id = "omls.components.mediaText";
        protected override void UpdateMedia() { }
    }

    [ComponentInfo(Id, "媒体控制按钮")]
    public sealed class MediaControllerComponent(IMediaStateService service) : MediaComponentBase<MediaControllerComponentSettings>(service)
    {
        public const string Id = "omls.components.mediaController";
        protected override void UpdateMedia() { }
    }

    [ComponentInfo(Id, "媒体封面")]
    public sealed class MediaCoverComponent(IMediaStateService service) : MediaComponentBase<MediaCoverComponentSettings>(service)
    {
        public const string Id = "omls.components.mediaCover";
        protected override void UpdateMedia() { }
    }

    [ComponentInfo(Id, "媒体进度条")]
    public sealed class MediaProgressComponent(IMediaStateService service) : MediaComponentBase<MediaProgressComponentSettings>(service)
    {
        public const string Id = "omls.components.mediaProgress";
        protected override void UpdateMedia() { }
    }

    public sealed class MediaSettingsControl : ControlWithSettingsBase<MediaComponentSettings>;
    public sealed class MediaTextSettingsControl : ControlWithSettingsBase<MediaTextComponentSettings>;
    public sealed class MediaControllerSettingsControl : ControlWithSettingsBase<MediaControllerComponentSettings>;
    public sealed class MediaCoverSettingsControl : ControlWithSettingsBase<MediaCoverComponentSettings>;
    public sealed class MediaProgressSettingsControl : ControlWithSettingsBase<MediaProgressComponentSettings>;
}

namespace OhMyLockScreen.Services.Media
{
    public sealed class WindowsMediaSessionSource : IMediaSessionSource
    {
        public event EventHandler? Changed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MediaState> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(MediaState.Empty);
        public Task<MediaState> ReadPlaceholderAsync(CancellationToken cancellationToken) => Task.FromResult(MediaState.Empty);
        public Task<bool> ExecuteAsync(MediaAction action, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken) => Task.FromResult(false);
        public void Dispose() { }
    }
}
