using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace OhMyLockScreen.Services.SystemStatus;

[GeneratedComInterface]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
internal partial interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(nint notification);
}

// Keep every preceding native slot, even though this component only reads volume and mute.
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal partial interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float level, nint context);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, nint context);
    [PreserveSig] int GetMasterVolumeLevel(out float level);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, nint context);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, nint context);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute(int mute, nint context);
    [PreserveSig] int GetMute(out int mute);
}
