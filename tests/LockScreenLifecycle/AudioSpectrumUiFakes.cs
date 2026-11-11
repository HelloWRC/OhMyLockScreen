using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Audio;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Models.Audio;

// Real composition rendering and platform interop run in ComponentBackgroundPreview.
namespace OhMyLockScreen.Controls.Components
{
    [ComponentInfo(Id, "音频频谱")]
    public sealed class AudioSpectrumComponent : ComponentBase<AudioSpectrumComponentSettings>
    {
        public const string Id = "omls.components.audioSpectrum";
        public AudioSpectrumComponent(IAudioSpectrumService spectrum, IMediaStateService media)
        {
            ArgumentNullException.ThrowIfNull(spectrum);
            ArgumentNullException.ThrowIfNull(media);
        }
    }

    public sealed class AudioSpectrumSettingsControl : ControlWithSettingsBase<AudioSpectrumComponentSettings>;
}

namespace OhMyLockScreen.Services.Audio
{
    public sealed class WindowsAudioSpectrumSource : IAudioSpectrumSource
    {
        public bool IsSuspended => false;
        public event EventHandler? Changed { add { } remove { } }
        public Task CaptureAsync(Action<AudioSpectrumSnapshot> publish, CancellationToken cancellationToken)
            => Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
