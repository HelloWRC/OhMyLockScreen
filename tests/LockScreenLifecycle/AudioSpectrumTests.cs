using System.Buffers.Binary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.Audio;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Audio;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.Audio;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.Storage;

internal static class AudioSpectrumTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Spectrum host registration and settings persistence", () =>
        {
            var factory = typeof(OhMyLockScreen.App).GetMethod("CreateHost", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            using var host = (Microsoft.Extensions.Hosting.IHost)factory.Invoke(null, null)!;
            var service = host.Services.GetRequiredService<IAudioSpectrumService>();
            Check(ReferenceEquals(service, host.Services.GetRequiredService<IAudioSpectrumService>()), "Service is not shared.");
            var components = host.Services.GetRequiredService<IComponentsService>();
            var settings = new ComponentSettings { Id = AudioSpectrumComponent.Id };
            Check(components.CreateComponent(settings) is AudioSpectrumComponent, "Component not registered.");
            var defaults = new AudioSpectrumComponentSettings();
            Check(defaults.Dock == SpectrumDock.Bottom && defaults.ColorSource == SpectrumColorSource.MediaCover
                && defaults.BarCount == 64 && defaults.Gap == 2 && defaults.Sensitivity == 1, "Incorrect defaults.");
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForCurrentAssembly();
            var root = new SceneSettings
            {
                Layers = [new LayerSettings { Children = [new ComponentSettings
            {
                Id = AudioSpectrumComponent.Id,
                Settings = new AudioSpectrumComponentSettings
                {
                    Dock = SpectrumDock.Right, ColorSource = SpectrumColorSource.Foreground,
                    BarCount = 37, Gap = 3.5, Sensitivity = 1.7
                }
            }] }]
            };
            var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "spectrum.json");
            ConfigureFileHelper.SaveConfig(path, root, true);
            var restored = (AudioSpectrumComponentSettings)ConfigureFileHelper.LoadConfig<SceneSettings>(path)!.Layers[0].Children[0].Settings!;
            Check(restored is { Dock: SpectrumDock.Right, ColorSource: SpectrumColorSource.Foreground, BarCount: 37, Gap: 3.5, Sensitivity: 1.7 }, "Settings lost values.");
            Check(File.ReadAllText(path).Contains("omls.audioSpectrum"), "Missing type discriminator.");
        });

        run("PCM integer, float, extensible and padded frame decoding", () =>
        {
            foreach (var bits in new[] { 8, 16, 24, 32 })
            {
                var bytes = new byte[bits / 8];
                bytes[^1] = bits == 8 ? (byte)192 : (byte)64;
                var format = AudioSampleFormat.Read(Format(bits, false));
                var values = new float[1];
                Check(format.Decode(bytes, values) == 1 && Math.Abs(values[0] - 0.5) < 1e-6, $"PCM {bits} scaling failed.");
                bytes[^1] = bits == 8 ? (byte)64 : (byte)192;
                format.Decode(bytes, values);
                Check(Math.Abs(values[0] + 0.5) < 1e-6, $"PCM {bits} sign failed.");
            }

            foreach (var bits in new[] { 32, 64 })
            {
                var data = bits == 32 ? BitConverter.GetBytes(0.25f) : BitConverter.GetBytes(0.25d);
                var values = new float[1];
                AudioSampleFormat.Read(Format(bits, true)).Decode(data, values);
                Check(values[0] == 0.25, "Float PCM failed.");
            }

            var extended = Format(32, false, true);
            BinaryPrimitives.WriteUInt16LittleEndian(extended.AsSpan(18), 24);
            var decoded = new float[1];
            AudioSampleFormat.Read(extended).Decode([0, 0, 0, 64], decoded);
            Check(decoded[0] == 0.5, "Left-aligned valid bits changed scale.");
            var padded = new AudioSampleFormat(48000, 1, 16, 4, false);
            var paddedValues = new float[2];
            padded.Decode([0, 64, 99, 99, 0, 192, 99, 99], paddedValues);
            Check(paddedValues[0] == 0.5 && paddedValues[1] == -0.5, "Block alignment ignored.");
            var nonFinite = new float[1];
            AudioSampleFormat.Read(Format(32, true)).Decode(BitConverter.GetBytes(float.NaN), nonFinite);
            Check(nonFinite[0] == 0, "NaN escaped decoder.");
            extended[24] = 99;
            try
            {
                AudioSampleFormat.Read(extended);
                throw new Exception("Unknown subformat accepted.");
            }
            catch (NotSupportedException)
            {
            }
        });

        run("FFT frequency, amplitude, channel power and reset", () =>
        {
            var analyzer = new SpectrumAnalyzer(48000, 2);
            var samples = new float[SpectrumAnalyzer.WindowSize * 2];
            const int bin = 85;
            for (var index = 0; index < SpectrumAnalyzer.WindowSize; ++index)
            {
                var value = (float)(0.5 * Math.Sin(2 * Math.PI * bin * index / SpectrumAnalyzer.WindowSize));
                samples[2 * index] = value;
                samples[2 * index + 1] = -value;
            }

            analyzer.Append(samples);
            var first = analyzer.Analyze(DateTimeOffset.UtcNow);
            var powers = first.Power.ToArray();
            Check(Array.IndexOf(powers, powers.Max()) == bin && Math.Abs(powers[bin] - 0.25) < 0.001,
                "FFT peak/amplitude incorrect or opposite channels canceled.");
            analyzer.Reset();
            Check(analyzer.Analyze(DateTimeOffset.UtcNow).Power.Span.IndexOfAnyExcept(0f) < 0, "Reset retained old audio.");
            Check(first.Power.Span[bin] == powers[bin], "Published snapshot mutated.");
        });

        run("Log bands, sensitivity, elapsed-time smoothing and stale-frame silence", () =>
        {
            var power = new float[2049];
            power[85] = 0.01f;
            var now = DateTimeOffset.UtcNow;
            var state = new AudioSpectrumSnapshot { IsAvailable = true, Power = power, SampleRate = 48000, Timestamp = now };
            var normal = new SpectrumPresentation();
            var sensitive = new SpectrumPresentation();
            normal.Update(state, 64, 1, TimeSpan.FromSeconds(1), now);
            sensitive.Update(state, 64, 2, TimeSpan.FromSeconds(1), now);
            Check(normal.HasSignal && sensitive.Levels.ToArray().Max() > normal.Levels.ToArray().Max(), "Sensitivity did not increase height.");
            var peak = Array.IndexOf(normal.Levels.ToArray(), normal.Levels.ToArray().Max());
            Check(peak is > 25 and < 45, "Frequency was not logarithmically positioned.");
            var before = normal.Levels.ToArray().Max();
            normal.Update(state, 64, 1, TimeSpan.FromMilliseconds(200), now.AddSeconds(1));
            Check(Math.Abs(normal.Levels.ToArray().Max() / before - Math.Exp(-1)) < 0.001, "Release is not time based.");
            for (var index = 0; index < 10; ++index)
            {
                normal.Update(state, 64, 1, TimeSpan.FromSeconds(1), now.AddSeconds(2));
            }

            Check(!normal.HasSignal, "Stale data left visible columns.");
            normal.Update(state, int.MaxValue, double.NaN, TimeSpan.FromSeconds(1), now);
            Check(normal.Levels.Length == 256 && normal.Levels.ToArray().All(double.IsFinite), "Invalid settings escaped bounds.");
        });

        run("Four docking directions and constrained gap geometry", () =>
        {
            var bottom = SpectrumPresentation.Arrange([0.25, 0.5], 100, 80, SpectrumDock.Bottom, 2);
            var top = SpectrumPresentation.Arrange([0.25, 0.5], 100, 80, SpectrumDock.Top, 2);
            var left = SpectrumPresentation.Arrange([0.25, 0.5], 100, 80, SpectrumDock.Left, 2);
            var right = SpectrumPresentation.Arrange([0.25, 0.5], 100, 80, SpectrumDock.Right, 2);
            Check(bottom[0].Y == 60 && bottom[0].Height == 20 && top[0].Y == 0, "Vertical growth incorrect.");
            Check(left[0].Width == 25 && left[0].X == 0 && right[0].X == 75, "Horizontal growth incorrect.");
            Check(bottom[1].X > bottom[0].X && left[1].Y > left[0].Y, "Frequency ordering reversed.");
            var tiny = SpectrumPresentation.Arrange(SpectrumPresentation.Placeholder(256), 10, 10, SpectrumDock.Bottom, 20);
            Check(tiny.All(bar => bar.Width > 0 && bar.X + bar.Width <= 10.0001), "Gap overflowed tiny control.");
            Check(SpectrumPresentation.Arrange([1], 0, 5, SpectrumDock.Bottom, 2).Length == 0, "Zero size rendered geometry.");
        });
    }

    internal static async Task RunAsync(Func<string, Func<Task>, Task> run)
    {
        await run("Spectrum pipe frames survive fragmented reads without raw audio or UI state", async () =>
        {
            var power = Enumerable.Range(0, 2049).Select(index => index / 2049f).ToArray();
            var state = new AudioSpectrumSnapshot
            {
                IsAvailable = true,
                Power = power,
                SampleRate = 48000,
                Timestamp = DateTimeOffset.UtcNow
            };
            using var bytes = new MemoryStream();
            await AudioSpectrumProtocol.WriteAsync(bytes, state, default);
            await AudioSpectrumProtocol.WriteAsync(bytes, AudioSpectrumSnapshot.Unavailable, default);
            using var fragmented = new FragmentedStream(bytes.ToArray());
            var restored = await AudioSpectrumProtocol.ReadAsync(fragmented, default);
            Check(restored.IsAvailable && restored.SampleRate == state.SampleRate && restored.Timestamp == state.Timestamp
                && restored.Power.Span.SequenceEqual(power), "Fragmented spectrum frame changed values.");
            Check(!((await AudioSpectrumProtocol.ReadAsync(fragmented, default)).IsAvailable), "Unavailable frame became audio.");
            Check(bytes.Length == 40 + 2049 * sizeof(float), "Protocol contains unexpected data.");
        });

        await run("Spectrum pipe rejects invalid lengths, invalid power and truncated payloads", async () =>
        {
            using var encoded = new MemoryStream();
            await AudioSpectrumProtocol.WriteAsync(encoded, new AudioSpectrumSnapshot
            {
                IsAvailable = true,
                Power = new float[2049],
                SampleRate = 48000,
                Timestamp = DateTimeOffset.UtcNow
            }, default);
            var bytes = encoded.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), int.MaxValue);
            await ExpectAsync<InvalidDataException>(bytes);
            bytes = encoded.ToArray();
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(20), float.NaN);
            await ExpectAsync<InvalidDataException>(bytes);
            await ExpectAsync<EndOfStreamException>(encoded.ToArray()[..25]);
            using var canceled = new MemoryStream();
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            try
            {
                await AudioSpectrumProtocol.ReadAsync(canceled, stop.Token);
                throw new InvalidOperationException("Pipe read ignored cancellation.");
            }
            catch (OperationCanceledException)
            {
            }
        });

        await run("Shared leases, device restart, suspension and stale callbacks", async () =>
        {
            var source = new Source();
            using var service = new AudioSpectrumService(source, NullLogger<AudioSpectrumService>.Instance, TimeProvider.System);
            Check(source.Starts == 0, "Captured before acquire.");
            var first = service.Acquire();
            var second = service.Acquire();
            await Until(() => source.Starts == 1);
            var old = source.Publish!;
            old(Frame(1));
            Check(service.State.SampleRate == 1, "Snapshot not published.");
            first.Dispose();
            Check(source.Stops == 0, "One lease stopped shared capture.");
            source.Notify();
            await Until(() => source.Starts == 2);
            Check(source.Stops == 1 && source.MaximumActive == 1, "Restart overlapped captures.");
            old(Frame(99));
            Check(service.State.SampleRate != 99, "Old source callback restored stale state.");
            source.IsSuspended = true;
            source.Notify();
            await Until(() => source.Stops == 2);
            await Task.Delay(30);
            Check(source.Starts == 2 && !service.State.IsAvailable, "Suspension restarted capture.");
            source.IsSuspended = false;
            source.Notify();
            await Until(() => source.Starts == 3);
            second.Dispose();
            await Until(() => source.Stops == 3);
            Check(!service.State.IsAvailable, "Release retained stale data.");
            using var third = service.Acquire();
            await Until(() => source.Starts == 4);
            await service.StopAsync(default);
            Check(source.Stops == 4 && source.MaximumActive == 1, "Host stop leaked capture.");
        });

        await run("Failure retry backoff and cancellation", async () =>
        {
            var source = new Source { Fail = true };
            var clock = new RetryClock();
            using var service = new AudioSpectrumService(source, NullLogger<AudioSpectrumService>.Instance, clock);
            using var lease = service.Acquire();
            await Until(() => clock.Delays.Count == 1);
            Check(clock.Delays[0] == TimeSpan.FromSeconds(1), "First retry is not one second.");
            clock.Fire();
            await Until(() => clock.Delays.Count == 2);
            Check(clock.Delays[1] == TimeSpan.FromSeconds(2), "Second retry is not two seconds.");
            clock.Fire();
            await Until(() => clock.Delays.Count == 3);
            Check(clock.Delays[2] == TimeSpan.FromSeconds(5), "Subsequent retry is not five seconds.");
            lease.Dispose();
            await service.StopAsync(default);
            Check(!service.State.IsAvailable, "Failure fabricated audio.");
        });

        await run("Rapid reacquire waits for the previous native cleanup", async () =>
        {
            var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var source = new Source { Cleanup = cleanup.Task };
            using var service = new AudioSpectrumService(source, NullLogger<AudioSpectrumService>.Instance, TimeProvider.System);
            var first = service.Acquire();
            await Until(() => source.Starts == 1);
            first.Dispose();
            using var second = service.Acquire();
            await Task.Delay(30);
            Check(source.Starts == 1, "New capture started before old cleanup completed.");
            cleanup.SetResult();
            await Until(() => source.Starts == 2);
            Check(source.MaximumActive == 1, "Rapid reload overlapped native resources.");
            await service.StopAsync(default);
        });
    }

    private static byte[] Format(int bits, bool floating, bool extended = false)
    {
        var bytes = new byte[extended ? 40 : 18];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)(extended ? 0xFFFE : floating ? 3 : 1));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 48000);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), (ushort)(bits / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), (ushort)bits);
        if (extended)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), 22);
            new Guid(floating ? "00000003-0000-0010-8000-00aa00389b71" : "00000001-0000-0010-8000-00aa00389b71").TryWriteBytes(bytes.AsSpan(24));
        }

        return bytes;
    }

    private static async Task ExpectAsync<T>(byte[] bytes) where T : Exception
    {
        using var stream = new MemoryStream(bytes);
        try
        {
            await AudioSpectrumProtocol.ReadAsync(stream, default);
            throw new InvalidOperationException($"Expected {typeof(T).Name}.");
        }
        catch (T)
        {
        }
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], cancellationToken);
    }

    private static AudioSpectrumSnapshot Frame(int rate) => new() { IsAvailable = true, SampleRate = rate, Timestamp = DateTimeOffset.UtcNow };
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private sealed class Source : IAudioSpectrumSource
    {
        public bool IsSuspended
        {
            get; set;
        }
        public bool Fail
        {
            get; init;
        }
        public Task Cleanup { get; init; } = Task.CompletedTask;
        public int Starts;
        public int Stops;
        public int Active;
        public int MaximumActive;
        public Action<AudioSpectrumSnapshot>? Publish;
        public event EventHandler? Changed;
        public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
        public async Task CaptureAsync(Action<AudioSpectrumSnapshot> publish, CancellationToken cancellationToken)
        {
            Publish = publish;
            Interlocked.Increment(ref Starts);
            if (Fail)
            {
                throw new InvalidOperationException("Simulated capture failure.");
            }
            MaximumActive = Math.Max(MaximumActive, Interlocked.Increment(ref Active));
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            finally
            {
                await Cleanup;
                Interlocked.Decrement(ref Active);
                Interlocked.Increment(ref Stops);
            }
        }
    }

    private sealed class RetryClock : TimeProvider
    {
        private readonly object _sync = new();
        private TimerCallback? _callback;
        private object? _state;
        internal List<TimeSpan> Delays { get; } = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_sync)
            {
                _callback = callback;
                _state = state;
                Delays.Add(dueTime);
            }

            return new TimerLease();
        }

        internal void Fire()
        {
            TimerCallback? callback;
            object? state;
            lock (_sync)
            {
                callback = _callback;
                state = _state;
            }
            callback!(state);
        }

        private sealed class TimerLease : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose()
            {
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
