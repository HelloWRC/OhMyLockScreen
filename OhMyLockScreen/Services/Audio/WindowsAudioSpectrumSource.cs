using System.Diagnostics;
using System.Runtime.InteropServices;
using OhMyLockScreen.Shared.Abstractions.Services.Audio;
using OhMyLockScreen.Shared.Models.Audio;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Media.Devices;

namespace OhMyLockScreen.Services.Audio;

public sealed class WindowsAudioSpectrumSource : IAudioSpectrumSource, IDisposable
{
    private readonly object _sync = new();
    private Task _capture = Task.CompletedTask;
    private volatile bool _suspended = false;
#if !AUDIO_HOST
    private readonly bool _useAudioHost;
#endif
    public bool IsSuspended => _suspended;
    public event EventHandler? Changed;

#if AUDIO_HOST
    public WindowsAudioSpectrumSource()
#else
    public WindowsAudioSpectrumSource(bool useAudioHost = true)
#endif
    {
        AudioSpectrumTrace.Write("Source created");
        MediaDevice.DefaultAudioRenderDeviceChanged += OnDeviceChanged;
#if !AUDIO_HOST
        _useAudioHost = useAudioHost;
        CoreApplication.Suspending += OnSuspending;
        CoreApplication.Resuming += OnResuming;
#endif
    }

    private void OnDeviceChanged(object sender, DefaultAudioRenderDeviceChangedEventArgs args)
    {
        if (args.Role == AudioDeviceRole.Default)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

#if !AUDIO_HOST
    private async void OnSuspending(object? sender, SuspendingEventArgs args)
    {
        var deferral = args.SuspendingOperation.GetDeferral();
        AudioSpectrumTrace.Write("Suspending");
        try
        {
            Task capture;
            lock (_sync)
            {
                _suspended = true;
                capture = _capture;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            try
            {
                await capture.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnResuming(object? sender, object args)
    {
        AudioSpectrumTrace.Write("Resuming");
        _suspended = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }
#endif

    public Task CaptureAsync(Action<AudioSpectrumSnapshot> publish, CancellationToken cancellationToken)
    {
#if !AUDIO_HOST
        if (_useAudioHost)
        {
            lock (_sync)
            {
                if (_suspended)
                {
                    throw new OperationCanceledException("应用正在挂起。", cancellationToken);
                }

                return _capture = AudioHostSpectrumClient.CaptureAsync(publish, cancellationToken);
            }
        }
#endif
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AudioSpectrumTrace.Write($"Capture requested; suspended={_suspended}");
        lock (_sync)
        {
            if (_suspended)
            {
                // Close the race between the supervisor's IsSuspended check and this call.
                throw new OperationCanceledException("应用正在挂起。", cancellationToken);
            }

            _capture = completion.Task;
        }
        var thread = new Thread(() =>
        {
            try
            {
                Capture(publish, cancellationToken);
                completion.TrySetResult();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "WASAPI spectrum capture"
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return completion.Task;
    }

    private static void Capture(Action<AudioSpectrumSnapshot> publish, CancellationToken cancellationToken)
    {
        Marshal.ThrowExceptionForHR(AudioInterop.CoInitializeEx(0, 0));
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        nint formatPointer = 0;
        var started = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
            if (string.IsNullOrEmpty(device))
            {
                throw new InvalidOperationException("没有默认音频输出设备。");
            }

            AudioSpectrumTrace.Write("Activating render client");
            var pointer = AudioInterop.ActivateClientAsync(device, cancellationToken).GetAwaiter().GetResult();
            AudioSpectrumTrace.Write("Render client activated");
            try
            {
                client = AudioInterop.WrapUnique<IAudioClient>(pointer);
            }
            finally
            {
                Marshal.Release(pointer);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(client.GetMixFormat(out formatPointer));
            var formatBytes = new byte[18 + (ushort)Marshal.ReadInt16(formatPointer, 16)];
            Marshal.Copy(formatPointer, formatBytes, 0, formatBytes.Length);
            var format = AudioSampleFormat.Read(formatBytes);
            AudioSpectrumTrace.Write($"Mix format: {format}");
            const uint loopback = 0x00020000;
            Marshal.ThrowExceptionForHR(client.Initialize(0, loopback, 1_000_000, 0, formatPointer, 0));
            var captureId = typeof(IAudioCaptureClient).GUID;
            Marshal.ThrowExceptionForHR(client.GetService(in captureId, out pointer));
            try
            {
                capture = AudioInterop.WrapUnique<IAudioCaptureClient>(pointer);
            }
            finally
            {
                Marshal.Release(pointer);
            }

            Marshal.ThrowExceptionForHR(client.GetBufferSize(out var capacity));
            var bytes = new byte[checked((int)capacity * format.BlockAlign)];
            var samples = new float[checked((int)capacity * format.Channels)];
            var analyzer = new SpectrumAnalyzer(format.SampleRate, format.Channels);
            Marshal.ThrowExceptionForHR(client.Start());
            started = true;
            AudioSpectrumTrace.Write("Loopback started");
            var clock = Stopwatch.StartNew();
            var lastPacket = TimeSpan.Zero;
            var nextPublish = TimeSpan.Zero;
            var silent = false;
            var nextTrace = TimeSpan.Zero;
            var packets = 0L;
            var silentPackets = 0L;
            while (!cancellationToken.IsCancellationRequested)
            {
                Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out var packetFrames));
                while (packetFrames > 0 && !cancellationToken.IsCancellationRequested)
                {
                    Marshal.ThrowExceptionForHR(capture.GetBuffer(out var data, out var frames, out var flags, out _, out _));
                    var count = checked((int)frames * format.BlockAlign);
                    ++packets;
                    if ((flags & 2) != 0)
                    {
                        ++silentPackets;
                    }
                    try
                    {
                        if (count > bytes.Length)
                        {
                            Array.Resize(ref bytes, count);
                            Array.Resize(ref samples, checked((int)frames * format.Channels));
                        }

                        if ((flags & 2) != 0)
                        {
                            Array.Clear(bytes, 0, count);
                        }
                        else if (count > 0)
                        {
                            Marshal.Copy(data, bytes, 0, count);
                        }
                    }
                    finally
                    {
                        Marshal.ThrowExceptionForHR(capture.ReleaseBuffer(frames));
                    }

                    if ((flags & 1) != 0)
                    {
                        analyzer.Reset();
                    }

                    var sampleCount = format.Decode(bytes.AsSpan(0, count), samples);
                    if ((flags & 2) != 0)
                    {
                        // Eight-bit PCM silence is 128, whereas WASAPI's SILENT flag is format independent.
                        Array.Clear(samples, 0, sampleCount);
                    }

                    analyzer.Append(samples.AsSpan(0, sampleCount));
                    lastPacket = clock.Elapsed;
                    silent = false;
                    Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out packetFrames));
                }

                var now = clock.Elapsed;
                if (!silent && now - lastPacket >= TimeSpan.FromMilliseconds(100))
                {
                    analyzer.Reset();
                    silent = true;
                }

                if (now >= nextPublish)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var snapshot = analyzer.Analyze(DateTimeOffset.UtcNow);
                    publish(snapshot);
                    if (now >= nextTrace)
                    {
                        var peak = 0f;
                        foreach (var power in snapshot.Power.Span)
                        {
                            peak = Math.Max(peak, power);
                        }

                        AudioSpectrumTrace.Write($"Capture: packets={packets}; silentPackets={silentPackets}; peak={peak:E3}; idleMs={(now - lastPacket).TotalMilliseconds:F0}");
                        nextTrace = now + TimeSpan.FromSeconds(5);
                    }
                    nextPublish = now + TimeSpan.FromSeconds(1d / 30);
                }

                cancellationToken.WaitHandle.WaitOne(10);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            AudioSpectrumTrace.Write("Releasing loopback");
            if (started)
            {
                client?.Stop();
            }

            AudioInterop.Release(capture);
            AudioInterop.Release(client);
            if (formatPointer != 0)
            {
                Marshal.FreeCoTaskMem(formatPointer);
            }

            AudioInterop.CoUninitialize();
        }
    }

    public void Dispose()
    {
        MediaDevice.DefaultAudioRenderDeviceChanged -= OnDeviceChanged;
#if !AUDIO_HOST
        CoreApplication.Suspending -= OnSuspending;
        CoreApplication.Resuming -= OnResuming;
#endif
    }
}
