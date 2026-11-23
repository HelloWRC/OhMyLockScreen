using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace OhMyLockScreen.Shared.Controls.Scene;

/// <summary>通过 ItemsControl 模板呈现图层中的组件。</summary>
public sealed class LayerPresenter : Control, ISceneBlobContext, IShadowTarget
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
        nameof(ParallaxHost), typeof(ISceneParallaxHost), typeof(LayerPresenter), new PropertyMetadata(null, OnParallaxHostChanged));

    public static readonly DependencyProperty ParallaxTransformProperty = DependencyProperty.Register(
        nameof(ParallaxTransform), typeof(CompositeTransform), typeof(LayerPresenter), new PropertyMetadata(null));

    private readonly List<(object Source, double? StrengthOverride)> _parallaxEntries = [];
    private FeatureAttacher? _featureAttacher;
    private ISceneParallaxHost? _registeredParallaxHost;
    private ISceneParallaxRegistration? _parallaxRegistration;

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

    /// <summary>用于模板内层容器的独立视差变换，避免与 RenderTransform 冲突。</summary>
    public CompositeTransform ParallaxTransform
    {
        get => (CompositeTransform)GetValue(ParallaxTransformProperty)!;
        private set => SetValue(ParallaxTransformProperty, value);
    }

    public LayerPresenter()
    {
        DefaultStyleKey = typeof(LayerPresenter);
        ParallaxTransform = new CompositeTransform();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>启用或更新来自指定特性实例的视差效果。</summary>
    public void SetParallax(object source, double? strengthOverride)
    {
        ArgumentNullException.ThrowIfNull(source);
        var index = _parallaxEntries.FindIndex(entry => ReferenceEquals(entry.Source, source));
        if (index >= 0)
        {
            _parallaxEntries[index] = (source, strengthOverride);
        }
        else
        {
            _parallaxEntries.Add((source, strengthOverride));
        }

        SyncParallaxRegistration();
    }

    /// <summary>移除指定特性实例施加的视差效果。</summary>
    public void ClearParallax(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var index = _parallaxEntries.FindIndex(entry => ReferenceEquals(entry.Source, source));
        if (index < 0)
        {
            return;
        }

        _parallaxEntries.RemoveAt(index);
        SyncParallaxRegistration();
    }

    private static void OnLayerChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var presenter = (LayerPresenter)sender;
        if (presenter._featureAttacher != null)
        {
            presenter._featureAttacher.FeatureContainer = presenter.Layer;
        }
    }

    private static void OnParallaxHostChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((LayerPresenter)sender).SyncParallaxRegistration();
    }

    private void SyncParallaxRegistration()
    {
        var host = _parallaxEntries.Count > 0 ? ParallaxHost : null;
        if (!ReferenceEquals(host, _registeredParallaxHost))
        {
            var previous = _parallaxRegistration;
            _parallaxRegistration = null;
            _registeredParallaxHost = null;
            previous?.Dispose();

            if (host != null)
            {
                var registration = host.Register(ParallaxTransform, _parallaxEntries[^1].StrengthOverride);
                _registeredParallaxHost = host;
                _parallaxRegistration = registration;
            }
        }
        else if (_parallaxRegistration != null)
        {
            _parallaxRegistration.SetStrengthOverride(_parallaxEntries[^1].StrengthOverride);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _featureAttacher ??= new FeatureAttacher(this) { FeatureContainer = Layer };
        _featureAttacher.Attach();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        var attacher = _featureAttacher;
        _featureAttacher = null;
        attacher?.Dispose();
    }
}
