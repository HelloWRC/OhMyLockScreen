using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.Scene;

/// <summary>通过 ItemsControl 模板呈现图层中的组件。</summary>
public sealed class LayerPresenter : Control, ISceneBlobContext, IDecoratorHost
{
    public static readonly DependencyProperty BlobUriConverterProperty = DependencyProperty.Register(
        nameof(BlobUriConverter), typeof(SceneBlobUriConverter), typeof(LayerPresenter),
        new PropertyMetadata(null, (sender, _) => ((LayerPresenter)sender)._featureAttacher?.Reload()));

    public SceneBlobUriConverter? BlobUriConverter
    {
        get => (SceneBlobUriConverter?)GetValue(BlobUriConverterProperty);
        set => SetValue(BlobUriConverterProperty, value);
    }

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing), typeof(bool), typeof(LayerPresenter), new PropertyMetadata(false));

    public bool IsEditing
    {
        get => GetValue(IsEditingProperty) is true;
        set => SetValue(IsEditingProperty, value);
    }

    public static readonly DependencyProperty LayerProperty = DependencyProperty.Register(
        nameof(Layer), typeof(LayerSettings), typeof(LayerPresenter), new PropertyMetadata(null, OnLayerChanged));

    public static readonly DependencyProperty ParallaxHostProperty = DependencyProperty.Register(
        nameof(ParallaxHost), typeof(ISceneParallaxHost), typeof(LayerPresenter), new PropertyMetadata(null));

    private FeatureAttacher? _featureAttacher;

    public DecoratorChain Decorators { get; } = new();

    public LayerSettings? Layer
    {
        get => (LayerSettings?)GetValue(LayerProperty);
        set => SetValue(LayerProperty, value);
    }

    /// <summary>驱动当前图层视差的场景宿主。</summary>
    public ISceneParallaxHost? ParallaxHost
    {
        get => (ISceneParallaxHost?)GetValue(ParallaxHostProperty);
        set => SetValue(ParallaxHostProperty, value);
    }

    public LayerPresenter()
    {
        DefaultStyleKey = typeof(LayerPresenter);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static void OnLayerChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (LayerPresenter)sender;
        if (presenter._featureAttacher != null)
        {
            presenter._featureAttacher.FeatureContainer = presenter.Layer;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _featureAttacher ??= new FeatureAttacher(this) { FeatureContainer = Layer };
        _featureAttacher.Attach();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (IsLoaded)
        {
            return;
        }

        var attacher = _featureAttacher;
        _featureAttacher = null;
        attacher?.Dispose();
    }
}
