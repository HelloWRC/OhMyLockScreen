using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace OhMyLockScreen.Services.SystemStatus;

internal static partial class AudioVolumeInterop
{
    [LibraryImport("Mmdevapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int ActivateAudioInterfaceAsync(string deviceInterfacePath, in Guid iid,
        nint activationParams, nint completionHandler, out nint activationOperation);

    internal static unsafe T WrapUnique<T>(nint pointer) => UniqueComInterfaceMarshaller<T>.ConvertToManaged((void*)pointer)!;

    internal static void Release(object? wrapper)
    {
        if (wrapper is ComObject instance)
        {
            instance.FinalRelease();
        }
    }
}

// Both callback implementations are thread-safe and may be called from any COM apartment.
[GeneratedComInterface]
[Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
internal partial interface IAgileObject
{
}

[GeneratedComInterface]
[Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
internal partial interface IActivateAudioInterfaceCompletionHandler
{
    [PreserveSig] int ActivateCompleted(nint operation);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
internal partial interface IActivateAudioInterfaceAsyncOperation
{
    [PreserveSig] int GetActivateResult(out int activateResult, out nint activatedInterface);
}

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
