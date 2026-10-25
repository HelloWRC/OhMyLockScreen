using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ScenePresenter : UserControl
{
    private const double UnlockDurationMilliseconds = 333;

    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(SceneSettings), typeof(ScenePresenter), new PropertyMetadata(null, OnSceneChanged));

    private FeatureAttacher? _featureAttacher;
    private Storyboard? _unlockStoryboard;
    private TaskCompletionSource? _unlockCompletion;
    private Task? _unlockAnimationTask;

    public SceneSettings? Scene
    {
        get => (SceneSettings?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public event EventHandler<string>? BackgroundFailed;

    public ScenePresenter()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public Task PlayUnlockAnimationAsync()
    {
        return _unlockAnimationTask ??= PlayUnlockAnimationCoreAsync();
    }

    private bool IsActive
    {
        get;
        set;
    }

    private static void OnSceneChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (ScenePresenter)sender;
        if (presenter._featureAttacher != null)
        {
            presenter._featureAttacher.FeatureContainer = presenter.Scene;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        IsActive = true;
        Bindings.Update();
        _featureAttacher ??= new FeatureAttacher(this) { FeatureContainer = Scene };
        _featureAttacher.Attach();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        IsActive = false;
        CancelUnlockAnimation();
        Bindings.Update();
        Bindings.StopTracking();

        var attacher = _featureAttacher;
        _featureAttacher = null;
        attacher?.Dispose();
    }

    private async Task PlayUnlockAnimationCoreAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storyboard = new Storyboard();
        _unlockCompletion = completion;
        _unlockStoryboard = storyboard;
        storyboard.Completed += OnUnlockStoryboardCompleted;

        try
        {
            AddUnlockAnimations(storyboard);
            storyboard.Begin();
        }
        catch (Exception exception)
        {
            CancelUnlockAnimation(exception);
        }

        await completion.Task;
    }

    private void AddUnlockAnimations(Storyboard storyboard)
    {
        AddDoubleAnimation(storyboard, LayersLayer, nameof(UIElement.Opacity), 1, 0);
        AddDoubleAnimation(storyboard, LayersTransform, nameof(TranslateTransform.Y), 0, -Math.Max(ActualHeight, 0));

        if (BackgroundLayer.Visibility == Visibility.Visible)
        {
            AddDoubleAnimation(storyboard, BackgroundLayer, nameof(UIElement.Opacity), 1, 0);
        }
    }

    private static void AddDoubleAnimation(
        Storyboard storyboard,
        DependencyObject target,
        string targetProperty,
        double from,
        double to)
    {
        var duration = TimeSpan.FromMilliseconds(UnlockDurationMilliseconds);
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(duration),
            FillBehavior = FillBehavior.HoldEnd
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
            Value = from
        });
        animation.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(duration),
            Value = to,
            KeySpline = new KeySpline
            {
                ControlPoint1 = new Point(0, 0),
                ControlPoint2 = new Point(0, 1)
            }
        });

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, targetProperty);
        storyboard.Children.Add(animation);
    }

    private void OnUnlockStoryboardCompleted(object? sender, object args)
    {
        if (sender is Storyboard storyboard)
        {
            storyboard.Completed -= OnUnlockStoryboardCompleted;
        }

        var completion = _unlockCompletion;
        _unlockStoryboard = null;
        _unlockCompletion = null;
        completion?.TrySetResult();
    }

    private void CancelUnlockAnimation(Exception? exception = null)
    {
        var storyboard = _unlockStoryboard;
        var completion = _unlockCompletion;
        _unlockStoryboard = null;
        _unlockCompletion = null;

        if (storyboard != null)
        {
            storyboard.Completed -= OnUnlockStoryboardCompleted;
            storyboard.Stop();
        }

        if (exception == null)
        {
            completion?.TrySetResult();
        }
        else
        {
            completion?.TrySetException(exception);
        }
    }

    private bool IsSoundMuted(bool enableSound)
    {
        return !enableSound;
    }

    private Visibility GetVideoVisibility(BackgroundMode mode, string? source)
    {
        return mode == BackgroundMode.Video && !string.IsNullOrWhiteSpace(source)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private Uri? GetVideoSource(Guid sceneId, BackgroundMode mode, string? source, bool isActive)
    {
        if (!isActive || mode != BackgroundMode.Video || string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        try
        {
            var service = IAppHost.GetRequiredService<ISceneService>();
            var directory = service.GetSceneDirectory(sceneId);
            var path = Path.GetFullPath(Path.Combine(directory, source));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("背景资源必须位于当前场景目录内。");
            }

            var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            var escaped = string.Join("/", relative.Split('/').Select(Uri.EscapeDataString));
            return new Uri($"ms-appdata:///local/Scenes/{sceneId:N}/{escaped}");
        }
        catch (Exception exception)
        {
            BackgroundFailed?.Invoke(this, $"无法加载背景视频：{exception.Message}");
            return null;
        }
    }

    private void OnMediaFailed(object sender, ExceptionRoutedEventArgs args)
    {
        BackgroundFailed?.Invoke(this, $"无法播放背景视频：{args.ErrorMessage}");
    }
}
