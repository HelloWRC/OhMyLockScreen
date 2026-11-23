using System;
using System.Diagnostics;
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

public sealed partial class ScenePresenter : UserControl, ISceneBlobContext
{
    public static readonly DependencyProperty BlobUriConverterProperty = DependencyProperty.Register(
        nameof(BlobUriConverter), typeof(SceneBlobUriConverter), typeof(ScenePresenter), new PropertyMetadata(null));

    public SceneBlobUriConverter? BlobUriConverter
    {
        get => (SceneBlobUriConverter?)GetValue(BlobUriConverterProperty);
        private set => SetValue(BlobUriConverterProperty, value);
    }

    private void UpdateBlobContext()
    {
        if (Scene?.SceneId == BlobUriConverter?.SceneId)
        {
            return;
        }

        BlobUriConverter = Scene is { } scene && IAppHost.TryGetService<ISceneBlobStorageService>() is { } storage
            ? new SceneBlobUriConverter(storage, scene.SceneId)
            : null;
    }

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing), typeof(bool), typeof(ScenePresenter), new PropertyMetadata(false));

    public bool IsEditing
    {
        get => GetValue(IsEditingProperty) is true;
        set => SetValue(IsEditingProperty, value);
    }

    internal static readonly TimeSpan UnlockDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan UnlockReturnDuration = TimeSpan.FromMilliseconds(180);

    private static readonly DependencyProperty UnlockReturnProgressProperty = DependencyProperty.Register(
        "UnlockReturnProgress", typeof(double), typeof(ScenePresenter), new PropertyMetadata(0d, OnUnlockReturnProgressChanged));

    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(SceneSettings), typeof(ScenePresenter), new PropertyMetadata(null, OnSceneChanged));

    private FeatureAttacher? _featureAttacher;
    private Storyboard? _unlockReturnStoryboard;
    private Storyboard? _unlockStoryboard;
    private TaskCompletionSource? _unlockCompletion;
    private Task? _unlockAnimationTask;
    private double _unlockTargetY;

    public SceneSettings? Scene
    {
        get => (SceneSettings?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public event EventHandler<string>? BackgroundFailed;

    public ScenePresenter()
    {
        InitializeComponent();
        InitializeParallax();
        InitializeBackgroundLayout();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public Task PlayUnlockAnimationAsync()
    {
        return _unlockAnimationTask ??= PlayUnlockAnimationCoreAsync();
    }

    internal bool IsReturningFromUnlockDrag => _unlockReturnStoryboard != null;

    private double UnlockTravel => Math.Max(ActualHeight, 0) / 4;

    internal void SetUnlockDragOffset(double upwardDistance)
    {
        double travel = UnlockTravel;
        double offset = Math.Clamp(upwardDistance, 0, travel);
        ApplyUnlockVisuals(-offset, travel > 0 ? 1 - offset / travel : 1);
    }

    internal void PlayUnlockReturnAnimation()
    {
        double progress = 1 - LayersLayer.Opacity;
        if (progress <= 0 || IsReturningFromUnlockDrag)
        {
            return;
        }

        var storyboard = new Storyboard();
        _unlockReturnStoryboard = storyboard;
        storyboard.Completed += OnUnlockReturnStoryboardCompleted;

        try
        {
            // A dependent progress animation keeps the rendered offset available for keyboard interruption.
            AddDoubleAnimation(storyboard, this, "UnlockReturnProgress", progress, 0, UnlockReturnDuration)
                .EnableDependentAnimation = true;
            storyboard.Begin();
        }
        catch (Exception exception)
        {
            ResetUnlockDrag();
            Debug.WriteLine($"无法启动锁屏回弹动画：{exception}");
        }
    }

    internal void ResetUnlockDrag()
    {
        StopUnlockReturnAnimation();
        ApplyUnlockVisuals(0, 1);
    }

    private static void OnUnlockReturnProgressChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (ScenePresenter)sender;
        var progress = (double)args.NewValue!;
        if (presenter.IsReturningFromUnlockDrag)
        {
            presenter.ApplyUnlockVisuals(-presenter.UnlockTravel * progress, 1 - progress);
        }
    }

    private void ApplyUnlockVisuals(double y, double opacity)
    {
        LayersTransform.Y = y;
        LayersLayer.Opacity = opacity;
        BackgroundViewport.Opacity = opacity;
        SetParallaxMultiplier(opacity);
    }

    private void OnUnlockReturnStoryboardCompleted(object? sender, object args)
    {
        ResetUnlockDrag();
    }

    private void StopUnlockReturnAnimation()
    {
        var storyboard = _unlockReturnStoryboard;
        _unlockReturnStoryboard = null;
        if (storyboard != null)
        {
            storyboard.Completed -= OnUnlockReturnStoryboardCompleted;
            storyboard.Stop();
        }
    }

    private bool IsActive
    {
        get;
        set;
    }

    private static void OnSceneChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (ScenePresenter)sender;
        presenter.UpdateBlobContext();
        if (presenter._featureAttacher != null)
        {
            presenter._featureAttacher.FeatureContainer = presenter.Scene;
        }

        if (presenter.IsActive)
        {
            if ((args.OldValue as SceneSettings)?.SceneId != (args.NewValue as SceneSettings)?.SceneId)
            {
                presenter.ResetParallaxMotion();
            }

            presenter.RefreshParallaxStrength();
            presenter.UpdateBackgroundSubscriptions();
            presenter.UpdateBackground();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        _parallaxClock = IAppHost.TryGetService<TimeProvider>() ?? TimeProvider.System;
        UpdateBlobContext();
        Bindings.Update();
        _featureAttacher ??= new FeatureAttacher(this) { FeatureContainer = Scene };
        _featureAttacher.Attach();
        RefreshParallaxStrength();
        AttachBackground();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        IsActive = false;
        RefreshParallaxStrength();
        DetachParallaxTimer();
        DetachBackground();
        CancelUnlockAnimation();
        ResetUnlockDrag();
        Bindings.Update();
        Bindings.StopTracking();

        var attacher = _featureAttacher;
        _featureAttacher = null;
        attacher?.Dispose();
    }

    private Task PlayUnlockAnimationCoreAsync()
    {
        StopUnlockReturnAnimation();
        StopParallaxTimer();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storyboard = new Storyboard();
        _unlockCompletion = completion;
        _unlockStoryboard = storyboard;
        _unlockTargetY = -UnlockTravel;
        storyboard.Completed += OnUnlockStoryboardCompleted;

        try
        {
            AddDoubleAnimation(storyboard, LayersTransform, nameof(TranslateTransform.Y), LayersTransform.Y, _unlockTargetY, UnlockDuration);
            AddDoubleAnimation(storyboard, LayersLayer, nameof(UIElement.Opacity), LayersLayer.Opacity, 0, UnlockDuration);
            AddDoubleAnimation(storyboard, BackgroundViewport, nameof(UIElement.Opacity), BackgroundViewport.Opacity, 0, UnlockDuration);
            AddParallaxUnlockAnimations(storyboard);

            storyboard.Begin();
        }
        catch (Exception exception)
        {
            CancelUnlockAnimation(exception);
        }

        return completion.Task;
    }

    private static DoubleAnimationUsingKeyFrames AddDoubleAnimation(
        Storyboard storyboard,
        DependencyObject target,
        string targetProperty,
        double from,
        double to,
        TimeSpan duration)
    {
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
                ControlPoint1 = new Point(0.1, 0.9),
                ControlPoint2 = new Point(0.2, 1)
            }
        });

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, targetProperty);
        storyboard.Children.Add(animation);
        return animation;
    }

    private void OnUnlockStoryboardCompleted(object? sender, object args)
    {
        ApplyUnlockVisuals(_unlockTargetY, 0);
        CancelUnlockAnimation();
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
}
