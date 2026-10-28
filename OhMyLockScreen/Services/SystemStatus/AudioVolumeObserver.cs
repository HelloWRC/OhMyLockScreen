using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using OhMyLockScreen.Shared.Models.SystemStatus;
using Windows.Media.Devices;

namespace OhMyLockScreen.Services.SystemStatus;

internal sealed partial class AudioVolumeObserver : IDisposable
{
    private readonly object _sync = new();
    private readonly Action<SystemStatusSnapshot> _publish;
    private readonly Action<Exception> _failure;
    private IAudioEndpointVolume? _endpoint;
    private VolumeCallback? _callback;
    private int _generation;
    private bool _disposed;

    public AudioVolumeObserver(Action<SystemStatusSnapshot> publish, Action<Exception> failure)
    {
        _publish = publish;
        _failure = failure;
        MediaDevice.DefaultAudioRenderDeviceChanged += OnDeviceChanged;
        Activate();
    }

    private void OnDeviceChanged(object sender, DefaultAudioRenderDeviceChangedEventArgs args)
    {
        if (args.Role == AudioDeviceRole.Default)
        {
            Activate();
        }
    }

    private void Activate()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            var generation = ++_generation;
            ClearEndpoint();
            try
            {
                var device = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
                if (string.IsNullOrEmpty(device))
                {
                    _publish(new SystemStatusSnapshot { Availability = SystemStatusAvailability.Missing });
                    return;
                }

                _publish(SystemStatusSnapshot.Unavailable);
                var completion = new Activation(this, generation);
                completion.Start(device);
            }
            catch (Exception exception)
            {
                _failure(exception);
            }
        }
    }

    private void CompleteActivation(int generation, IAudioEndpointVolume? endpoint, Exception? error)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation || error != null)
            {
                AudioVolumeInterop.Release(endpoint);
                if (!_disposed && generation == _generation && error != null)
                {
                    _failure(error);
                }

                return;
            }

            try
            {
                _endpoint = endpoint!;
                _callback = new VolumeCallback(this, generation);
                Marshal.ThrowExceptionForHR(_endpoint.RegisterControlChangeNotify(_callback));
                RefreshVolume(generation);
            }
            catch (Exception exception)
            {
                ClearEndpoint();
                _failure(exception);
            }
        }
    }

    private void RefreshVolume(int generation)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation || _endpoint == null)
            {
                return;
            }

            try
            {
                // Re-read the current endpoint so queued notifications cannot reorder old values.
                Marshal.ThrowExceptionForHR(_endpoint.GetMasterVolumeLevelScalar(out var volume));
                Marshal.ThrowExceptionForHR(_endpoint.GetMute(out var mute));
                _publish(new SystemStatusSnapshot
                {
                    Availability = SystemStatusAvailability.Available,
                    Percent = (int)Math.Round(Math.Clamp(volume, 0, 1) * 100, MidpointRounding.AwayFromZero),
                    IsMuted = mute != 0
                });
            }
            catch (Exception exception)
            {
                _failure(exception);
            }
        }
    }

    private void ClearEndpoint()
    {
        if (_endpoint != null)
        {
            if (_callback != null)
            {
                _endpoint.UnregisterControlChangeNotify(_callback);
            }

            AudioVolumeInterop.Release(_endpoint);
            _endpoint = null;
            _callback = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ++_generation;
            MediaDevice.DefaultAudioRenderDeviceChanged -= OnDeviceChanged;
            ClearEndpoint();
        }
    }

    [GeneratedComClass]
    internal sealed partial class VolumeCallback(AudioVolumeObserver owner, int generation) : IAudioEndpointVolumeCallback, IAgileObject
    {
        public int OnNotify(nint notification)
        {
            try
            {
                // Native OnNotify must never block on a lock held by unregister/dispose.
                ThreadPool.QueueUserWorkItem(_ => owner.RefreshVolume(generation));
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception);
            }

            return 0;
        }
    }

    [GeneratedComClass]
    internal sealed partial class Activation(AudioVolumeObserver owner, int generation) : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        private readonly object _operationSync = new();
        private nint _operation;
        private bool _completed;

        public unsafe void Start(string device)
        {
            var pointer = ComInterfaceMarshaller<IActivateAudioInterfaceCompletionHandler>.ConvertToUnmanaged(this);
            try
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(AudioVolumeInterop.ActivateAudioInterfaceAsync(device, in iid, 0, (nint)pointer, out var operation));
                lock (_operationSync)
                {
                    // Completion may precede the return from ActivateAudioInterfaceAsync.
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
                ComInterfaceMarshaller<IActivateAudioInterfaceCompletionHandler>.Free(pointer);
            }
        }

        public int ActivateCompleted(nint operation)
        {
            IActivateAudioInterfaceAsyncOperation? wrapper = null;
            nint activated = 0;
            IAudioEndpointVolume? endpoint = null;
            Exception? error = null;
            try
            {
                wrapper = AudioVolumeInterop.WrapUnique<IActivateAudioInterfaceAsyncOperation>(operation);
                Marshal.ThrowExceptionForHR(wrapper.GetActivateResult(out var result, out activated));
                Marshal.ThrowExceptionForHR(result);
                endpoint = AudioVolumeInterop.WrapUnique<IAudioEndpointVolume>(activated);
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                if (activated != 0)
                {
                    Marshal.Release(activated);
                }

                AudioVolumeInterop.Release(wrapper);
            }

            try
            {
                owner.CompleteActivation(generation, endpoint, error);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception);
            }
            finally
            {
                lock (_operationSync)
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
}
