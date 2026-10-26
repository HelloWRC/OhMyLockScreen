using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.Services;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.Storage;
using Windows.UI.Xaml;

internal static class VideoBackgroundTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Video waits for its own swapchain and preserves path, mute and crop settings", () =>
        {
            using var fixture = new Fixture();
            var presenter = fixture.Presenter;
            var view = fixture.View;
            Check(view.MediaPlayer == null, "Playback started before the swapchain was initialized.");
            Check(!view.IsHitTestVisible && !view.IsTabStop, "The background intercepted input.");
            view.Initialize("--winrt-swapchain=first", "--winrt-d3dcontext=first");
            var player = view.MediaPlayer!;
            Check(player.LibVlc.Options.SequenceEqual(["--winrt-swapchain=first", "--winrt-d3dcontext=first"]),
                "The player did not use its view's swapchain.");
            Check(player.Media!.Source == fixture.VideoPath && player.Media.Type == FromType.FromPath,
                "Local paths containing spaces, Chinese and URI characters were changed.");
            Check(player.Mute && player.IsPlaying, "The background did not start muted.");

            presenter.BackgroundLayerElement.Resize(1920, 1080);
            player.OutputVideo();
            presenter.Dispatcher.Drain();
            Check(player.CropGeometry == "1920:1080", "Video did not crop to the viewport aspect ratio.");
            presenter.BackgroundLayerElement.Resize(1080, 1920);
            Check(player.CropGeometry == "1080:1920", "Resizing did not update crop geometry.");
            fixture.Scene.Background.EnableSound = true;
            Check(!player.Mute && player.PlayCalls == 1, "Changing sound restarted the video.");
            presenter.Load();
            Check(ReferenceEquals(view, fixture.View) && player.PlayCalls == 1, "Repeated Loaded restarted playback.");
        });

        run("Video restarts after every end and sets fill cropping before the next frame", () =>
        {
            using var fixture = new Fixture();
            var presenter = fixture.Presenter;
            presenter.BackgroundLayerElement.Resize(1920, 1200);
            var view = fixture.View;
            view.Initialize("loop");
            var player = view.MediaPlayer!;
            var media = player.Media;
            Check(player.CropAtPlay == "1920:1200", "Initial playback started before fill cropping was set.");
            fixture.Scene.Background.EnableSound = true;

            for (var cycle = 1; cycle <= 3; cycle++)
            {
                presenter.BackgroundLayerElement.Resize(1920 + cycle, 1200);
                player.CropGeometry = null;
                player.Finish();
                player.Finish();
                Check(!player.IsPlaying && player.PlayCalls == cycle && player.StopCalls == cycle - 1,
                    "An end callback synchronously stopped or restarted native playback.");
                presenter.Dispatcher.Drain();
                Check(player.IsPlaying && player.PlayCalls == cycle + 1 && player.StopCalls == cycle,
                    "An end did not restart playback exactly once.");
                Check(player.CropAtPlay == $"{1920 + cycle}:1200" && !player.Mute,
                    "Loop playback lost fill cropping or the current sound setting.");
                Check(ReferenceEquals(player.Media, media) && ReferenceEquals(view, fixture.View)
                    && !player.LibVlc.IsDisposed, "Looping unnecessarily replaced the media or rendering resources.");

                player.CropGeometry = null;
                player.OutputVideo();
                presenter.Dispatcher.Drain();
                Check(player.CropGeometry == $"{1920 + cycle}:1200", "A new video output did not restore fill cropping.");
            }
        });

        run("Video output destruction and stale output callbacks do not change cropping", () =>
        {
            using var fixture = new Fixture();
            fixture.Presenter.BackgroundLayerElement.Resize(1600, 1000);
            fixture.View.Initialize("output");
            var player = fixture.View.MediaPlayer!;
            player.OutputVideo();
            player.OutputVideo(0);
            player.CropGeometry = null;
            fixture.Presenter.Dispatcher.Drain();
            Check(player.CropGeometry == null, "Crop was written after video output had been destroyed.");
            player.OutputVideo();
            fixture.Presenter.Dispatcher.Drain();
            Check(player.CropGeometry == "1600:1000", "A valid replacement output did not get fill cropping.");
        });

        run("A video ending during suspension restarts only after its view resumes", () =>
        {
            using var fixture = new Fixture();
            fixture.View.Initialize("suspended-end");
            var player = fixture.View.MediaPlayer!;
            player.Finish();
            fixture.Application.Suspend();
            fixture.Presenter.Dispatcher.Drain();
            Check(!player.IsPlaying && player.StopCalls == 0 && player.PlayCalls == 1,
                "An ended video restarted while suspended.");
            fixture.Application.Resume();
            fixture.Presenter.Dispatcher.Drain();
            Check(player.IsPlaying && player.StopCalls == 1 && player.PlayCalls == 2,
                "Resume unpaused an ended input instead of restarting it.");
        });

        run("Queued end callbacks cannot restart replaced, disabled or reloaded backgrounds", () =>
        {
            using var fixture = new Fixture();
            fixture.View.Initialize("old");
            var first = fixture.View.MediaPlayer!;
            first.Finish();
            fixture.AddVideo("replacement.mkv");
            fixture.Scene.Background.Source = "Assets/replacement.mkv";
            fixture.View.Initialize("replacement");
            var second = fixture.View.MediaPlayer!;
            fixture.Presenter.Dispatcher.Drain();
            Check(first.IsDisposed && first.PlayCalls == 1 && second.PlayCalls == 1,
                "A replaced player's end callback restarted playback.");
            second.Finish();
            fixture.Scene.Background.Mode = BackgroundMode.None;
            fixture.Presenter.Dispatcher.Drain();
            Check(second.IsDisposed && second.PlayCalls == 1
                && fixture.Presenter.BackgroundLayerElement.Children.Count == 0,
                "Disabling the background did not cancel queued looping.");
            fixture.Scene.Background.Mode = BackgroundMode.Video;
            fixture.View.Initialize("before-unload");
            var third = fixture.View.MediaPlayer!;
            third.Finish();
            fixture.Presenter.Unload();
            fixture.Presenter.Load();
            fixture.View.Initialize("reloaded");
            var fourth = fixture.View.MediaPlayer!;
            fixture.Presenter.Dispatcher.Drain();
            Check(third.IsDisposed && third.PlayCalls == 1 && fourth.PlayCalls == 1 && fourth.IsPlaying,
                "An earlier view lifetime restarted the reloaded background.");
        });

        run("Loop restart failure reports once and releases the current player", () =>
        {
            using var fixture = new Fixture();
            fixture.View.Initialize("restart-failure");
            var player = fixture.View.MediaPlayer!;
            var failures = 0;
            fixture.Presenter.BackgroundFailed += (_, message) =>
            {
                Check(message.StartsWith("无法播放背景视频："), "Restart failure lost its diagnostic prefix.");
                failures++;
            };
            MediaPlayer.FailOnPlay = true;
            player.Finish();
            player.Finish();
            fixture.Presenter.Dispatcher.Drain();
            Check(failures == 1 && player.IsDisposed && player.LibVlc.IsDisposed && player.Subscribers == 0,
                "Failed looping did not report once and release subscriptions and native resources.");
        });

        run("Video settings and scene replacements release old players and detach old models", () =>
        {
            using var fixture = new Fixture();
            var oldView = fixture.View;
            oldView.Initialize("first");
            var first = oldView.MediaPlayer!;
            var oldBackground = fixture.Scene.Background;
            var secondPath = fixture.AddVideo("second.mkv");
            fixture.Scene.Background = new BackgroundSettings
            {
                Mode = BackgroundMode.Video,
                Source = "Assets/second.mkv",
                EnableSound = true
            };
            Check(first.IsDisposed && first.LibVlc.IsDisposed && first.StopCalls == 1 && first.Subscribers == 0,
                "Replacing settings did not release the old player and libvlc.");
            var secondView = fixture.View;
            oldView.Initialize("stale");
            oldBackground.Source = "Assets/obsolete.mp4";
            Check(ReferenceEquals(secondView, fixture.View), "An old model or swapchain changed the new video.");
            secondView.Initialize("second");
            var second = secondView.MediaPlayer!;
            Check(second.Media!.Source == secondPath && !second.Mute, "Replacement settings were not applied.");

            fixture.Scene.Background.Source = "Assets/背景 #100%.mp4";
            Check(second.IsDisposed && !ReferenceEquals(secondView, fixture.View), "Changing source kept the old player.");
            var thirdView = fixture.View;
            thirdView.Initialize("third");
            var third = thirdView.MediaPlayer!;
            fixture.Presenter.Scene = new SceneSettings();
            Check(third.IsDisposed && fixture.Presenter.BackgroundLayerElement.Visibility == Visibility.Collapsed,
                "Switching to a scene without video did not stop playback.");
            fixture.Scene.Background.Source = "Assets/second.mkv";
            Check(fixture.Presenter.BackgroundLayerElement.Children.Count == 0, "An old scene remained subscribed.");
        });

        run("Queued native errors cannot stop a replacement video or an unloaded presenter", () =>
        {
            using var fixture = new Fixture();
            var presenter = fixture.Presenter;
            var firstView = fixture.View;
            firstView.Initialize("first");
            firstView.MediaPlayer!.Fail();
            fixture.AddVideo("second.webm");
            fixture.Scene.Background.Source = "Assets/second.webm";
            var secondView = fixture.View;
            secondView.Initialize("second");
            var second = secondView.MediaPlayer!;
            string? failure = null;
            presenter.BackgroundFailed += (_, message) => failure = message;
            presenter.Dispatcher.Drain();
            Check(failure == null && !second.IsDisposed, "A queued old error stopped the replacement video.");
            second.Fail();
            presenter.Unload();
            presenter.Dispatcher.Drain();
            Check(failure == null && second.IsDisposed, "An unloaded view handled an old playback error.");
            presenter.Load();
            var reloadedView = fixture.View;
            Check(!ReferenceEquals(secondView, reloadedView), "Reload reused a destroyed swapchain.");
            reloadedView.Initialize("reloaded");
            Check(reloadedView.MediaPlayer!.IsPlaying, "Reload did not resume a fresh player.");
        });

        run("Current native errors and synchronous startup failure report once and release playback", () =>
        {
            using var fixture = new Fixture();
            string? failure = null;
            var failures = 0;
            fixture.Presenter.BackgroundFailed += (_, message) => { failure = message; failures++; };
            var view = fixture.View;
            view.Initialize("first");
            var player = view.MediaPlayer!;
            player.Fail();
            player.Fail();
            Check(failure == null, "A native callback changed the UI synchronously.");
            fixture.Presenter.Dispatcher.Drain();
            Check(failures == 1 && failure!.StartsWith("无法播放背景视频：") && player.IsDisposed,
                "Playback errors were not reported once or did not release the player.");

            MediaPlayer.FailOnPlay = true;
            fixture.Scene.Background.EnableSound = true;
            var failedView = fixture.View;
            failedView.Initialize("failed");
            Check(failures == 2 && failedView.MediaPlayer == null
                && fixture.Presenter.BackgroundLayerElement.Children.Count == 0,
                "Synchronous playback failure left a video or failed to report.");
        });

        foreach (var source in new[] { "../outside.mp4", "Assets/missing.mp4" })
        {
            run($"Video rejects invalid background source {source}", () =>
            {
                using var fixture = new Fixture();
                fixture.View.Initialize("first");
                var player = fixture.View.MediaPlayer!;
                string? failure = null;
                fixture.Presenter.BackgroundFailed += (_, message) => failure = message;
                fixture.Scene.Background.Source = source;
                Check(failure?.StartsWith("无法加载背景视频：") == true && player.IsDisposed
                    && fixture.Presenter.BackgroundLayerElement.Children.Count == 0,
                    "Invalid source did not report an error and release previous playback.");
            });
        }

        run("Disabling video, clearing source and clearing the scene remove playback", () =>
        {
            using var fixture = new Fixture();
            fixture.View.Initialize("first");
            var first = fixture.View.MediaPlayer!;
            fixture.Scene.Background.Mode = BackgroundMode.None;
            Check(first.IsDisposed && fixture.Presenter.BackgroundLayerElement.Children.Count == 0,
                "Disabling video kept playback alive.");
            fixture.Scene.Background.Mode = BackgroundMode.Video;
            fixture.View.Initialize("second");
            var second = fixture.View.MediaPlayer!;
            fixture.Scene.Background.Source = " ";
            Check(second.IsDisposed && fixture.Presenter.BackgroundLayerElement.Children.Count == 0,
                "An empty source kept playback alive.");
            fixture.Presenter.Scene = null;
            Check(fixture.Presenter.BackgroundLayerElement.Visibility == Visibility.Collapsed,
                "A null scene did not hide video.");
        });

        run("Suspension pauses and resumes only current playback, including deferred initialization", () =>
        {
            using var fixture = new Fixture();
            fixture.Application.Suspend();
            fixture.View.Initialize("deferred");
            Check(fixture.View.MediaPlayer == null, "Video started while suspended.");
            fixture.Application.Resume();
            fixture.Presenter.Dispatcher.Drain();
            var player = fixture.View.MediaPlayer!;
            Check(player.IsPlaying, "Deferred initialization did not start on resume.");
            fixture.Application.Suspend();
            Check(!player.IsPlaying, "Suspension did not pause the video.");
            fixture.Application.Resume();
            fixture.Presenter.Dispatcher.Drain();
            Check(player.IsPlaying && player.PlayCalls == 1, "Resume restarted rather than unpaused the video.");
            player.SetPause(true);
            fixture.Application.Suspend();
            fixture.Application.Resume();
            fixture.Presenter.Dispatcher.Drain();
            Check(!player.IsPlaying, "Resume started a video that was already paused.");
        });

        run("Concurrent presenters own independent swapchains and players", () =>
        {
            using var fixture = new Fixture();
            var firstWindow = Window.Current;
            var secondWindow = new Window();
            Window.Current = secondWindow;
            var second = new ScenePresenter { Scene = fixture.Scene };
            second.Load();
            var secondView = (VideoView)second.BackgroundLayerElement.Children.Single();
            secondView.Initialize("two");
            var secondPlayer = secondView.MediaPlayer!;
            Window.Current = firstWindow;
            fixture.View.Initialize("one");
            var firstPlayer = fixture.View.MediaPlayer!;
            Check(!ReferenceEquals(firstPlayer.LibVlc, secondPlayer.LibVlc), "Views shared libvlc render targets.");
            var suspension = fixture.Application.Suspend();
            Check(!firstPlayer.IsPlaying && secondPlayer.IsPlaying && !suspension.SuspendingOperation.Deferral.IsComplete,
                "Cross-view suspension did not wait for the owning dispatcher.");
            Window.Current = secondWindow;
            second.Dispatcher.Drain();
            Check(!secondPlayer.IsPlaying && suspension.SuspendingOperation.Deferral.IsComplete,
                "Cross-view suspension did not pause and complete its deferral.");
            second.Unload();
            Check(secondPlayer.IsDisposed, "The second view did not release playback.");
            fixture.Application.Resume();
            Window.Current = firstWindow;
            fixture.Presenter.Dispatcher.Drain();
            Check(firstPlayer.IsPlaying, "The first view did not resume independently.");
            Check(secondPlayer.IsDisposed && firstPlayer.IsPlaying, "Unloading one view affected another view.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal Application Application { get; } = new();
        internal ScenePresenter Presenter { get; }
        internal SceneSettings Scene { get; }
        internal string VideoPath { get; }
        internal VideoView View => (VideoView)Presenter.BackgroundLayerElement.Children.Single();
        private readonly SceneService _scenes;
        private readonly SettingsService _settings;

        internal Fixture()
        {
            var directory = ApplicationData.Current.LocalFolder.Path;
            _settings = new SettingsService(NullLogger<SettingsService>.Instance, Path.Combine(directory, "Settings.json"));
            _settings.LoadSettingsAsync().GetAwaiter().GetResult();
            _scenes = new SceneService(_settings, NullLogger<SceneService>.Instance, directory);
            _scenes.Initialize();
            IAppHost.Host = new HostBuilder().ConfigureLogging(logging => logging.AddDebug())
                .ConfigureServices(services =>
                {
                    services.AddSingleton(_settings);
                    services.AddSingleton<ISceneService>(_scenes);
                    services.AddSingleton<FeatureRegistryService>();
                    services.AddSingleton<IFeatureService, FeatureService>();
                }).Build();
            Scene = new SceneSettings { SceneId = _scenes.ActiveSceneId };
            VideoPath = AddVideo("背景 #100%.mp4");
            Scene.Background.Mode = BackgroundMode.Video;
            Scene.Background.Source = "Assets/背景 #100%.mp4";
            Presenter = new ScenePresenter { Scene = Scene };
            Presenter.Load();
        }

        internal string AddVideo(string name)
        {
            var assets = Path.Combine(_scenes.GetSceneDirectory(Scene.SceneId), "Assets");
            Directory.CreateDirectory(assets);
            var path = Path.Combine(assets, name);
            File.WriteAllText(path, "simulated video");
            return path;
        }

        public void Dispose()
        {
            Presenter.Unload();
            _scenes.Dispose();
            _settings.Dispose();
        }
    }
}
