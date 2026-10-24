using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;

namespace OhMyLockScreen.Shared.Controls.Scene;

public sealed partial class ScenePresenter : UserControl
{
    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(SceneSettings), typeof(ScenePresenter), new PropertyMetadata(null, OnSceneChanged));

    private FeatureAttacher? _featureAttacher;

    public SceneSettings? Scene
    {
        get => (SceneSettings?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public ScenePresenter()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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
        _featureAttacher ??= new FeatureAttacher(this) { FeatureContainer = Scene };
        _featureAttacher.Attach();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        var attacher = _featureAttacher;
        _featureAttacher = null;
        attacher?.Dispose();
    }
}
