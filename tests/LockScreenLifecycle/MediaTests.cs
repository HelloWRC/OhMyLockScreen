using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Services.FeatureHost;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.Media;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.Media;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.Storage;
using Windows.UI.Xaml;

internal static class MediaTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Media time projection respects rate, pause, nonzero starts and track bounds", () =>
        {
            var now = DateTimeOffset.UtcNow;
            var state = new MediaState
            {
                HasSession = true, IsPlaying = true, StartTime = TimeSpan.FromSeconds(10),
                EndTime = TimeSpan.FromSeconds(100), Position = TimeSpan.FromSeconds(20),
                LastUpdatedTime = now, PlaybackRate = 2
            };
            Check(state.GetPosition(now.AddSeconds(5)) == TimeSpan.FromSeconds(30), "Playback rate was ignored.");
            Check(state.GetText(MediaTextField.Position, now.AddSeconds(5)) == "0:20", "Elapsed time included the source start offset.");
            Check(state.GetText(MediaTextField.Remaining, now.AddSeconds(5)) == "1:10", "Remaining time was incorrect.");
            Check(state.GetPosition(now.AddHours(1)) == state.EndTime, "Projection escaped the track bounds.");
            Check((state with { IsPlaying = false }).GetPosition(now.AddHours(1)) == state.Position, "Paused progress advanced.");
            Check((state with { EndTime = TimeSpan.FromHours(2) }).GetText(MediaTextField.Duration, now) == "1:59:50", "Hour formatting failed.");
            Check(MediaState.Empty.GetText(MediaTextField.Position, now) == "", "No session showed a fabricated timestamp.");
            var liveStream = state with { EndTime = TimeSpan.Zero };
            Check(liveStream.GetText(MediaTextField.Position, now.AddSeconds(5)) == "0:20"
                && liveStream.GetText(MediaTextField.Duration, now) == "", "Unknown duration erased the live stream's elapsed time.");
        });

        run("Media theme extraction ignores transparency, retains raw colors and has a neutral fallback", () =>
        {
            Check(MediaThemeColor.Extract([0, 0, 255, 0, 200, 80, 10, 255, 201, 81, 11, 255]) == 0xFF0A50C8,
                "The extracted color was darkened or dominated by transparent pixels.");
            Check(MediaThemeColor.Extract([10, 20, 30, 0]) == 0xFF606060, "Transparent covers had no safe fallback.");
            Check(MediaThemeColor.Extract([]) == 0xFF606060, "Empty covers had no safe fallback.");
        });

        run("Presenter mirrors only the current component and restores visibility on editing and unload", () =>
        {
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddSingleton<FeatureRegistryService>();
                services.AddSingleton<IFeatureService, FeatureService>();
            }).Build();
            var service = new ObservableMediaService();
            var first = CreateComponent(service);
            var presenter = new ComponentPresenter();
            presenter.Load();
            presenter.Content = first;
            Check(presenter.Visibility == Visibility.Collapsed, "The component's initial visibility was ignored.");
            presenter.IsEditing = true;
            Check(presenter.Visibility == Visibility.Visible && first.IsEditing, "Editing did not restore visibility and propagate context.");
            presenter.IsEditing = false;
            Check(presenter.Visibility == Visibility.Collapsed, "Preview did not restore synchronization.");
            var second = CreateComponent(new ObservableMediaService { State = new MediaState { HasSession = true } });
            presenter.Content = second;
            Check(presenter.Visibility == Visibility.Visible, "Replacement retained the old component's visibility.");
            first.Visibility = Visibility.Visible;
            first.Visibility = Visibility.Collapsed;
            Check(presenter.Visibility == Visibility.Visible, "An old component still controlled the presenter.");
            presenter.Unload();
            Check(ReferenceEquals(presenter.ReadLocalValue(UIElement.VisibilityProperty), DependencyProperty.UnsetValue), "Unloading left a local visibility override.");
        });

        run("Media components recover while hidden and editor placeholders stay local to each view", () =>
        {
            var service = new ObservableMediaService();
            var firstWindow = Window.Current;
            var live = CreateComponent(service);
            live.Load();
            Check(live.Visibility == Visibility.Collapsed, "An empty session did not hide the live component.");
            var editorWindow = new Window();
            Window.Current = editorWindow;
            var editor = CreateComponent(service);
            editor.IsEditing = true;
            editor.Load();
            Check(editor.Visibility == Visibility.Visible && editor.Placeholder && editor.Current.Title == "Preview",
                "Editor did not select its placeholder.");
            Check(!service.State.HasSession && live.Visibility == Visibility.Collapsed, "Editor placeholder contaminated the singleton.");
            service.State = new MediaState { HasSession = true, Title = "Paused", IsPlaying = false };
            Check(editor.Current.Title == "Paused" && !editor.Placeholder, "An editor ignored the live session.");
            Window.Current = firstWindow;
            live.Dispatcher.Drain();
            Check(live.Visibility == Visibility.Visible && live.Current.Title == "Paused", "A hidden component did not recover or pause hid it.");
            service.State = MediaState.Empty;
            live.Settings.HideWhenNoMedia = false;
            Check(live.Visibility == Visibility.Visible && !live.Current.HasSession, "Disabling hiding fabricated media data.");
            Window.Current = editorWindow;
            editor.Dispatcher.Drain();
            editor.IsEditing = false;
            Check(editor.Visibility == Visibility.Collapsed && !editor.Placeholder, "Fullscreen preview retained editor fallback.");
            editor.IsEditing = true;
            editor.Unload();
            service.State = new MediaState { HasSession = true, Title = "After unload" };
            Check(editor.Current.Title == "Preview", "An unloaded component still refreshed.");
            Window.Current = firstWindow;
            live.Dispatcher.Drain();
            live.Unload();
        });

        run("Shared media settings refresh on each owning view and discard queued updates after unload", () =>
        {
            var service = new ObservableMediaService();
            var settings = new MediaTextComponentSettings();
            var firstWindow = Window.Current;
            var first = CreateComponent(service);
            first.SetSettings(settings);
            first.Load();
            var secondWindow = new Window();
            Window.Current = secondWindow;
            var second = CreateComponent(service);
            second.SetSettings(settings);
            second.Load();
            Window.Current = firstWindow;
            settings.HideWhenNoMedia = false;
            Check(first.Visibility == Visibility.Visible && second.Visibility == Visibility.Collapsed,
                "A shared setting wrote to another view synchronously.");
            Window.Current = secondWindow;
            second.Dispatcher.Drain();
            Check(second.Visibility == Visibility.Visible, "A shared setting never reached the second view.");
            settings.HideWhenNoMedia = true;
            Window.Current = firstWindow;
            first.Unload();
            first.Dispatcher.Drain();
            Check(first.Visibility == Visibility.Visible, "An unloaded component applied queued UI updates.");
            Window.Current = secondWindow;
            second.Unload();
        });

        run("Media settings round trip with independent part options and default hiding", () =>
        {
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForCurrentAssembly();
            var root = new ComponentSettings
            {
                Id = ContainerComponent.Id,
                Settings = new ContainerComponentSettings
                {
                    Children =
                    [
                        new() { Id = MediaComponent.Id, Settings = new MediaComponentSettings { Variant = MediaVariant.ImmersiveFull } },
                        new() { Id = MediaTextComponent.Id, Settings = new MediaTextComponentSettings { Field = MediaTextField.Remaining } },
                        new() { Id = MediaControllerComponent.Id, Settings = new MediaControllerComponentSettings { Action = MediaAction.Next } },
                        new() { Id = MediaCoverComponent.Id, Settings = new MediaCoverComponentSettings { ShowSideDim = false } },
                        new() { Id = MediaProgressComponent.Id, Settings = new MediaProgressComponentSettings { HideWhenNoMedia = false } }
                    ]
                }
            };
            var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "media.json");
            ConfigureFileHelper.SaveConfig(path, root, true);
            var restored = ConfigureFileHelper.LoadConfigUnWrapped<ComponentSettings>(path).GetSettings<ContainerComponentSettings>().Children;
            Check(restored[0].GetSettings<MediaComponentSettings>().Variant == MediaVariant.ImmersiveFull, "Variant did not persist.");
            Check(restored[1].GetSettings<MediaTextComponentSettings>().Field == MediaTextField.Remaining, "Text field did not persist.");
            Check(restored[2].GetSettings<MediaControllerComponentSettings>().Action == MediaAction.Next, "Button action did not persist.");
            Check(!restored[3].GetSettings<MediaCoverComponentSettings>().ShowSideDim, "Cover option did not persist.");
            Check(!restored[4].GetSettings<MediaProgressComponentSettings>().HideWhenNoMedia, "Hide option did not persist.");
            Check(new MediaComponentSettings().HideWhenNoMedia && new MediaCoverComponentSettings().HideWhenNoMedia,
                "New media settings did not default to hiding.");
            Check(!File.ReadAllText(path).Contains("IsEditing"), "Editing context leaked into settings.");
        });
    }

    internal static async Task RunAsync(Func<string, Func<Task>, Task> run)
    {
        await run("Media service publishes projected timeline snapshots and releases its shared timer", async () =>
        {
            var clock = new MediaClock();
            var source = new SessionSource
            {
                Current = new MediaState
                {
                    HasSession = true,
                    IsPlaying = true,
                    Position = TimeSpan.FromSeconds(14),
                    EndTime = TimeSpan.FromSeconds(239),
                    LastUpdatedTime = clock.GetUtcNow()
                }
            };
            using var service = new MediaStateService(source, clock, NullLogger<MediaStateService>.Instance);
            await service.StartAsync(default);
            var first = service.State;
            clock.Advance(TimeSpan.FromSeconds(3));
            Check(!ReferenceEquals(first, service.State) && service.State.Position == TimeSpan.FromSeconds(17),
                "A timer tick failed to publish a new bindable timeline snapshot.");
            service.Dispose();
            var final = service.State;
            clock.Advance(TimeSpan.FromSeconds(3));
            Check(ReferenceEquals(final, service.State) && clock.TimerDisposed, "A disposed timer still published media state.");
        });

        await run("Media service rejects old asynchronous reads after a newer track completes", async () =>
        {
            var source = new SessionSource();
            using var service = CreateService(source);
            await service.StartAsync(default);
            var oldRead = new TaskCompletionSource<MediaState>(TaskCreationOptions.RunContinuationsAsynchronously);
            var newRead = new TaskCompletionSource<MediaState>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.Reads.Enqueue(() => oldRead.Task);
            source.Notify();
            await UntilAsync(() => source.ReadCount == 2);
            source.Reads.Enqueue(() => newRead.Task);
            source.Notify();
            await UntilAsync(() => source.ReadCount == 3);
            newRead.SetResult(new MediaState { HasSession = true, Title = "New", ThemeColor = 0xFFFF0000 });
            await UntilAsync(() => service.State.Title == "New");
            oldRead.SetResult(new MediaState { HasSession = true, Title = "Old", ThemeColor = 0xFF0000FF });
            await Task.Delay(30);
            Check(service.State.Title == "New" && service.State.ThemeColor == 0xFFFF0000, "A stale read replaced the current track.");
            source.Current = MediaState.Empty;
            source.Notify();
            await UntilAsync(() => !service.State.HasSession);
            service.Dispose();
            Check(source.Disposed, "Stopping the service retained Windows subscriptions.");
        });

        await run("Media service gates controls, clamps seeks and recovers after failed operations", async () =>
        {
            var source = new SessionSource
            {
                Current = new MediaState
                {
                    HasSession = true, CanPlayPause = true, CanSeek = true,
                    MinSeekTime = TimeSpan.FromSeconds(5), MaxSeekTime = TimeSpan.FromSeconds(15)
                }
            };
            using var service = CreateService(source);
            await service.StartAsync(default);
            Check(!await service.ExecuteAsync(MediaAction.Next) && source.Operations == 0, "An unsupported action reached Windows.");
            Check(await service.ExecuteAsync(MediaAction.PlayPause) && source.Operations == 1, "Supported playback control failed.");
            await service.SeekAsync(TimeSpan.FromDays(1));
            Check(source.LastSeek == TimeSpan.FromSeconds(15), "Seek escaped the upper bound.");
            await service.SeekAsync(TimeSpan.FromSeconds(-1));
            Check(source.LastSeek == TimeSpan.FromSeconds(5), "Seek escaped the lower bound.");
            source.FailOperation = true;
            Check(!await service.ExecuteAsync(MediaAction.PlayPause) && service.State.HasSession, "Operation failure escaped or erased the current session.");
        });
    }

    private static MediaStateService CreateService(SessionSource source) => new(source, TimeProvider.System, NullLogger<MediaStateService>.Instance);

    private static ProbeComponent CreateComponent(IMediaStateService service)
    {
        var component = new ProbeComponent(service);
        component.SetSettings(new MediaTextComponentSettings());
        return component;
    }

    private static async Task UntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ProbeComponent(IMediaStateService service) : MediaComponentBase<MediaTextComponentSettings>(service)
    {
        public MediaState Current { get; private set; } = MediaState.Empty;
        public bool Placeholder { get; private set; }
        protected override void UpdateMedia()
        {
            Current = State;
            Placeholder = IsPlaceholder;
        }
    }

    private sealed class ObservableMediaService : IMediaStateService
    {
        private MediaState _state = MediaState.Empty;
        public MediaState State
        {
            get => _state;
            set { _state = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State))); }
        }
        public MediaState Placeholder { get; } = new() { HasSession = true, Title = "Preview" };
        public event PropertyChangedEventHandler? PropertyChanged;
        public Task<bool> ExecuteAsync(MediaAction action, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class SessionSource : IMediaSessionSource
    {
        public MediaState Current { get; set; } = MediaState.Empty;
        public ConcurrentQueue<Func<Task<MediaState>>> Reads { get; } = new();
        private int _readCount;
        public int ReadCount => Volatile.Read(ref _readCount);
        public int Operations { get; private set; }
        public TimeSpan LastSeek { get; private set; }
        public bool FailOperation { get; set; }
        public bool Disposed { get; private set; }
        public event EventHandler? Changed;
        public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MediaState> ReadPlaceholderAsync(CancellationToken cancellationToken) => Task.FromResult(MediaState.Empty);
        public Task<MediaState> ReadAsync(CancellationToken cancellationToken)
        {
            var read = Reads.TryDequeue(out var next) ? next() : Task.FromResult(Current);
            Interlocked.Increment(ref _readCount);
            return read;
        }
        public Task<bool> ExecuteAsync(MediaAction action, CancellationToken cancellationToken)
        {
            Operations++;
            return FailOperation ? Task.FromException<bool>(new InvalidOperationException("Simulated Windows failure")) : Task.FromResult(true);
        }
        public Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken)
        {
            LastSeek = position;
            return Task.FromResult(true);
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class MediaClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        private MediaTimer? _timer;
        public bool TimerDisposed => _timer?.Disposed == true;
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => _timer = new MediaTimer(callback, state);
        public void Advance(TimeSpan elapsed)
        {
            _now += elapsed;
            _timer?.Tick();
        }

        private sealed class MediaTimer(TimerCallback callback, object? state) : ITimer
        {
            public bool Disposed { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
            public void Tick()
            {
                if (!Disposed)
                {
                    callback(state);
                }
            }
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
