using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reactive.Disposables;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.ApplicationModel;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ScenePresenter
{
    private SceneSettings? _subscribedScene;
    private BackgroundSettings? _subscribedBackground;
    private Application? _backgroundApplication;
    private VideoView? _videoView;
    private LibVLC? _libVlc;
    private MediaPlayer? _backgroundPlayer;
    private IDisposable? _backgroundEvents;
    private string? _backgroundPath;
    private string[]? _swapChainOptions;
    private bool _backgroundSuspended;
    private bool _resumeBackground;
    private bool _backgroundRestartPending;
    private int _backgroundLifetime;

    private void AttachBackground()
    {
        _backgroundLifetime++;
        _backgroundApplication = Application.Current;
        if (_backgroundApplication != null)
        {
            _backgroundApplication.Suspending += OnBackgroundSuspending;
            _backgroundApplication.Resuming += OnBackgroundResuming;
        }

        BackgroundLayer.SizeChanged += OnBackgroundSizeChanged;
        UpdateBackgroundSubscriptions();
        UpdateBackground();
    }

    private void DetachBackground()
    {
        _backgroundLifetime++;
        if (_backgroundApplication != null)
        {
            _backgroundApplication.Suspending -= OnBackgroundSuspending;
            _backgroundApplication.Resuming -= OnBackgroundResuming;
            _backgroundApplication = null;
        }

        BackgroundLayer.SizeChanged -= OnBackgroundSizeChanged;
        UpdateBackgroundSubscriptions();
        ReleaseBackground();
        _backgroundSuspended = false;
    }

    private void UpdateBackgroundSubscriptions()
    {
        if (_subscribedScene != null)
        {
            _subscribedScene.PropertyChanged -= OnBackgroundSettingsChanged;
        }

        if (_subscribedBackground != null)
        {
            _subscribedBackground.PropertyChanged -= OnBackgroundSettingsChanged;
        }

        _subscribedScene = IsActive ? Scene : null;
        _subscribedBackground = _subscribedScene?.Background;
        if (_subscribedScene != null)
        {
            _subscribedScene.PropertyChanged += OnBackgroundSettingsChanged;
        }

        if (_subscribedBackground != null)
        {
            _subscribedBackground.PropertyChanged += OnBackgroundSettingsChanged;
        }
    }

    private void OnBackgroundSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (ReferenceEquals(sender, _subscribedScene)
            && !string.IsNullOrEmpty(args.PropertyName)
            && args.PropertyName is not nameof(SceneSettings.Background) and not nameof(SceneSettings.SceneId))
        {
            return;
        }

        if (Dispatcher.HasThreadAccess)
        {
            UpdateBackgroundSubscriptions();
            UpdateBackground();
        }
        else
        {
            DispatchBackground(() =>
            {
                UpdateBackgroundSubscriptions();
                UpdateBackground();
            });
        }
    }

    private void UpdateBackground()
    {
        if (!IsActive || Scene is not { Background.Mode: BackgroundMode.Video } scene
            || string.IsNullOrWhiteSpace(scene.Background.Source))
        {
            ReleaseBackground();
            return;
        }

        try
        {
            var service = IAppHost.GetRequiredService<ISceneService>();
            var directory = Path.GetFullPath(service.GetSceneDirectory(scene.SceneId));
            var path = Path.GetFullPath(Path.Combine(directory, scene.Background.Source));
            if (!path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("背景资源必须位于当前场景目录内。");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("背景视频文件不存在。", path);
            }

            if (_videoView != null && string.Equals(path, _backgroundPath, StringComparison.OrdinalIgnoreCase))
            {
                if (_backgroundPlayer != null)
                {
                    _backgroundPlayer.Mute = !scene.Background.EnableSound;
                }

                return;
            }

            ReleaseBackground();
            _backgroundPath = path;
            _videoView = new VideoView { IsHitTestVisible = false, IsTabStop = false };
            _videoView.Initialized += OnBackgroundInitialized;
            BackgroundLayer.Visibility = Visibility.Visible;
            BackgroundLayer.Children.Add(_videoView);
        }
        catch (Exception exception)
        {
            FailBackground($"无法加载背景视频：{exception.Message}");
        }
    }

    private void OnBackgroundInitialized(object? sender, InitializedEventArgs args)
    {
        if (!IsActive || !ReferenceEquals(sender, _videoView))
        {
            return;
        }

        ReleaseBackgroundPlayer();
        _swapChainOptions = args.SwapChainOptions;
        StartBackgroundPlayer();
    }

    private void StartBackgroundPlayer()
    {
        if (_backgroundSuspended || _swapChainOptions == null || _backgroundPath == null || _videoView == null)
        {
            return;
        }

        try
        {
            _libVlc = new LibVLC(_swapChainOptions);
            var player = new MediaPlayer(_libVlc)
            {
                Mute = Scene?.Background.EnableSound != true
            };
            _backgroundPlayer = player;
            // LibVLCSharp sends its internal event manager as sender, not the player.
            EventHandler<EventArgs> onError = (_, _) => OnBackgroundPlaybackError(player);
            EventHandler<EventArgs> onEnd = (_, _) => OnBackgroundEndReached(player);
            EventHandler<MediaPlayerVoutEventArgs> onOutput = (_, args) => OnBackgroundVideoOutput(player, args);
            player.EncounteredError += onError;
            player.EndReached += onEnd;
            player.Vout += onOutput;
            _backgroundEvents = Disposable.Create(() =>
            {
                player.EncounteredError -= onError;
                player.EndReached -= onEnd;
                player.Vout -= onOutput;
            });
            player.SetVideoTitleDisplay(Position.Disable, 0);
            _videoView.MediaPlayer = player;

            using var media = new Media(_libVlc, _backgroundPath, FromType.FromPath);
            UpdateBackgroundCrop();
            if (!player.Play(media))
            {
                FailBackground("无法播放背景视频：播放器无法启动。");
            }
        }
        catch (Exception exception)
        {
            FailBackground($"无法播放背景视频：{exception.Message}");
        }
    }

    private void OnBackgroundEndReached(MediaPlayer player)
    {
        // Stop/Play must run outside libvlc's input callback to avoid deadlocks.
        DispatchBackground(() =>
        {
            if (ReferenceEquals(player, _backgroundPlayer)
                && player.State is VLCState.Ended or VLCState.Stopped)
            {
                _backgroundRestartPending = true;
                if (!_backgroundSuspended)
                {
                    RestartBackgroundPlayer(player);
                }
            }
        });
    }

    private void RestartBackgroundPlayer(MediaPlayer player)
    {
        try
        {
            player.Stop();
            UpdateBackgroundCrop();
            _backgroundRestartPending = false;
            if (!player.Play())
            {
                FailBackground("无法播放背景视频：播放器无法重新启动。");
            }
        }
        catch (Exception exception)
        {
            FailBackground($"无法播放背景视频：{exception.Message}");
        }
    }

    private void OnBackgroundPlaybackError(MediaPlayer player)
    {
        // Always leave the native callback before stopping or disposing libvlc.
        DispatchBackground(() =>
        {
            if (ReferenceEquals(player, _backgroundPlayer))
            {
                FailBackground("无法播放背景视频：libvlc 无法解码或读取此视频。");
            }
        });
    }

    private void OnBackgroundVideoOutput(MediaPlayer player, MediaPlayerVoutEventArgs args)
    {
        if (args.Count == 0)
        {
            return;
        }

        DispatchBackground(() =>
        {
            if (ReferenceEquals(player, _backgroundPlayer) && player.VoutCount > 0)
            {
                UpdateBackgroundCrop();
            }
        });
    }

    private void OnBackgroundSizeChanged(object sender, SizeChangedEventArgs args) => UpdateBackgroundCrop();

    private void UpdateBackgroundCrop()
    {
        if (_backgroundPlayer != null && BackgroundLayer.ActualWidth > 0 && BackgroundLayer.ActualHeight > 0)
        {
            var width = Math.Max(1, (int)Math.Round(BackgroundLayer.ActualWidth));
            var height = Math.Max(1, (int)Math.Round(BackgroundLayer.ActualHeight));
            _backgroundPlayer.CropGeometry = string.Create(CultureInfo.InvariantCulture, $"{width}:{height}");
        }
    }

    private async void OnBackgroundSuspending(object sender, SuspendingEventArgs args)
    {
        if (Dispatcher.HasThreadAccess)
        {
            PauseBackground();
        }
        else
        {
            var deferral = args.SuspendingOperation.GetDeferral();
            var lifetime = _backgroundLifetime;
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (IsActive && lifetime == _backgroundLifetime)
                    {
                        PauseBackground();
                    }
                });
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"无法暂停背景播放：{exception}");
            }
            finally
            {
                deferral.Complete();
            }
        }
    }

    private void PauseBackground()
    {
        if (_backgroundSuspended)
        {
            return;
        }

        _backgroundSuspended = true;
        _resumeBackground = _backgroundPlayer?.State is VLCState.Opening or VLCState.Buffering or VLCState.Playing;
        if (_resumeBackground)
        {
            _backgroundPlayer?.SetPause(true);
        }
    }

    private void OnBackgroundResuming(object? sender, object args)
    {
        DispatchBackground(() =>
        {
            _backgroundSuspended = false;
            if (_backgroundPlayer == null)
            {
                StartBackgroundPlayer();
            }
            else if (_backgroundRestartPending)
            {
                RestartBackgroundPlayer(_backgroundPlayer);
            }
            else if (_resumeBackground)
            {
                _backgroundPlayer.SetPause(false);
            }

            _resumeBackground = false;
        });
    }

    private async void DispatchBackground(Action action)
    {
        var lifetime = _backgroundLifetime;
        try
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (IsActive && lifetime == _backgroundLifetime)
                {
                    action();
                }
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法调度背景播放更新：{exception}");
        }
    }

    private void FailBackground(string message)
    {
        ReleaseBackground();
        BackgroundFailed?.Invoke(this, message);
    }

    private void ReleaseBackgroundPlayer()
    {
        var player = _backgroundPlayer;
        _backgroundPlayer = null;
        var events = _backgroundEvents;
        _backgroundEvents = null;
        events?.Dispose();
        if (_videoView != null)
        {
            _videoView.MediaPlayer = null;
        }

        if (player != null)
        {
            try
            {
                player.Stop();
            }
            finally
            {
                player.Dispose();
            }
        }

        _libVlc?.Dispose();
        _libVlc = null;
        _resumeBackground = false;
        _backgroundRestartPending = false;
    }

    private void ReleaseBackground()
    {
        ReleaseBackgroundPlayer();
        if (_videoView != null)
        {
            _videoView.Initialized -= OnBackgroundInitialized;
            BackgroundLayer.Children.Remove(_videoView);
            _videoView = null;
        }

        _backgroundPath = null;
        _swapChainOptions = null;
        BackgroundLayer.Visibility = Visibility.Collapsed;
    }
}
