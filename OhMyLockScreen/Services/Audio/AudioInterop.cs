using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace OhMyLockScreen.Services.Audio;

internal static partial class AudioInterop
{
    [LibraryImport("Mmdevapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int ActivateAudioInterfaceAsync(string deviceInterfacePath, in Guid iid,
        nint activationParams, nint completionHandler, out nint activationOperation);

    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(nint reserved, uint apartment);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();

    internal static unsafe T WrapUnique<T>(nint pointer) => UniqueComInterfaceMarshaller<T>.ConvertToManaged((void*)pointer)!;

    internal static void Release(object? wrapper)
    {
        if (wrapper is ComObject instance)
        {
            instance.FinalRelease();
        }
    }

    /// <summary>允许取消等待，仍由回调收回迟到的接口与操作引用。</summary>
    internal static async Task<nint> ActivateClientAsync(string device, CancellationToken cancellationToken)
    {
        var activation = new ClientActivation();
        activation.Start(device);
        try
        {
            return await activation.Result.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            activation.Abandon();
            throw;
        }
    }
}

[GeneratedComInterface]
[Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
internal partial interface IAgileObject;

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

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
internal partial interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint flags, long bufferDuration, long periodicity, nint format, nint session);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint frames);
    [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closestMatch);
    [PreserveSig] int GetMixFormat(out nint format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(nint handle);
    [PreserveSig] int GetService(in Guid iid, out nint service);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
internal partial interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

[GeneratedComClass]
internal sealed partial class ClientActivation : IActivateAudioInterfaceCompletionHandler, IAgileObject
{
    private readonly object _sync = new();
    private nint _operation;
    private bool _completed;
    private bool _abandoned;
    internal TaskCompletionSource<nint> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal unsafe void Start(string device)
    {
        var callback = ComInterfaceMarshaller<IActivateAudioInterfaceCompletionHandler>.ConvertToUnmanaged(this);
        try
        {
            var iid = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(AudioInterop.ActivateAudioInterfaceAsync(device, in iid, 0, (nint)callback, out var operation));
            lock (_sync)
            {
                if (_completed)
                {
                    Marshal.Release(operation);
                }
                else
                {
                    _operation = operation;
                }
            }
        }
        finally
        {
            ComInterfaceMarshaller<IActivateAudioInterfaceCompletionHandler>.Free(callback);
        }
    }

    internal void Abandon()
    {
        lock (_sync)
        {
            _abandoned = true;
            if (Result.Task.IsCompletedSuccessfully)
            {
                Marshal.Release(Result.Task.Result);
            }
        }
    }

    public int ActivateCompleted(nint operation)
    {
        IActivateAudioInterfaceAsyncOperation? wrapper = null;
        nint activated = 0;
        try
        {
            wrapper = AudioInterop.WrapUnique<IActivateAudioInterfaceAsyncOperation>(operation);
            Marshal.ThrowExceptionForHR(wrapper.GetActivateResult(out var result, out activated));
            Marshal.ThrowExceptionForHR(result);
            lock (_sync)
            {
                if (!_abandoned)
                {
                    Result.TrySetResult(activated);
                    activated = 0;
                }
            }
        }
        catch (Exception exception)
        {
            Result.TrySetException(exception);
            // Also observe a fault when cancellation already abandoned the wait.
            _ = Result.Task.Exception;
        }
        finally
        {
            if (activated != 0)
            {
                Marshal.Release(activated);
            }

            AudioInterop.Release(wrapper);
            lock (_sync)
            {
                _completed = true;
                if (_operation != 0)
                {
                    Marshal.Release(_operation);
                    _operation = 0;
                }
            }
        }

        return 0;
    }
}
