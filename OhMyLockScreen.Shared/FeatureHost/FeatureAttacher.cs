using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Services.FeatureHost;
using ReactiveUI.Binding;

namespace OhMyLockScreen.Shared.FeatureHost;

/// <summary>
/// 为任意宿主组合特性挂载能力。调用和配置更新应在同一线程进行；UI 宿主应使用其 UI 线程。
/// </summary>
public sealed class FeatureAttacher : ObservableObject, IDisposable
{
    private readonly object _host;
    private readonly IFeatureService _featureService;
    private readonly FeatureRegistryService _registry;
    private readonly ILogger<FeatureAttacher> _logger;
    private readonly List<(string Id, FeatureBase Feature)> _attachedFeatures = [];
    private IFeatureContainer? _featureContainer;
    private IDisposable? _subscription;
    private FeatureInfo? _initializingFeature;
    private bool _isAttached;
    private bool _isUpdating;
    private bool _updateRequested;
    private bool _isDisposed;

    public FeatureAttacher(object host)
        : this(
            host ?? throw new ArgumentNullException(nameof(host)),
            IAppHost.GetRequiredService<IFeatureService>(),
            IAppHost.GetRequiredService<FeatureRegistryService>(),
            IAppHost.GetRequiredService<ILogger<FeatureAttacher>>())
    {
    }

    public FeatureAttacher(
        object host,
        IFeatureService featureService,
        FeatureRegistryService registry,
        ILogger<FeatureAttacher>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(featureService);
        ArgumentNullException.ThrowIfNull(registry);
        _host = host;
        _featureService = featureService;
        _registry = registry;
        _logger = logger ?? NullLogger<FeatureAttacher>.Instance;
    }

    /// <summary>特性配置来源。赋值不会激活挂载；已激活时替换此属性会全量重载。</summary>
    public IFeatureContainer? FeatureContainer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            return _featureContainer;
        }
        set
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            SetProperty(ref _featureContainer, value);
        }
    }

    /// <summary>按配置顺序挂载支持的特性，并开始监听配置；重复调用不会重复挂载。</summary>
    public void Attach()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        var subscription = new SingleAssignmentDisposable();
        _subscription = subscription;
        try
        {
            subscription.Disposable = Observable.Switch(this
                    .WhenAnyValue(attacher => attacher.FeatureContainer)
                    .Select(ObserveContainer))
                .Subscribe(
                    _ => RequestUpdate(),
                    exception => _logger.LogError(exception, "无法监听特性配置"));
        }
        catch
        {
            if (ReferenceEquals(_subscription, subscription))
            {
                StopObserving();
                _isAttached = false;
                RequestUpdate();
            }

            subscription.Dispose();
            throw;
        }
    }

    /// <summary>停止监听，并按挂载顺序逆序卸载特性。</summary>
    public void Detach()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _isAttached = false;
        StopObserving();
        RequestUpdate();
    }

    /// <summary>重新创建并挂载所有特性；未激活时不执行操作。</summary>
    public void Reload()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isAttached)
        {
            RequestUpdate();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _isAttached = false;
        StopObserving();
        RequestUpdate();
    }

    private IObservable<Unit> ObserveContainer(IFeatureContainer? container)
    {
        return container == null
            ? Observable.Return(Unit.Default)
            : Observable.Switch(container
                .WhenAnyValue(settings => settings.Features)
                .Select(ObserveFeatures));
    }

    private IObservable<Unit> ObserveFeatures(ObservableCollection<FeatureInfo> features)
    {
        return Observable.Create<Unit>(observer =>
        {
            var initializing = true;
            var subscription = features.ToObservableChangeSet()
                .Publish(changes => Observable.Merge(
                    // 同一配置对象可出现多次；属性只监听一次，挂载仍按原集合逐项进行。
                    changes.DistinctValues(feature => feature)
                        .AutoRefresh(feature => feature.Id)
                        .AutoRefreshOnObservable(feature => feature
                            .WhenPropertyChanged(settings => settings.Settings, notifyOnInitialValue: false)
                            .Where(_ => !ReferenceEquals(_initializingFeature, feature)))
                        .Where(changeSet => changeSet.Refreshes != 0)
                        .Select(_ => Unit.Default),
                    changes.Select(_ => Unit.Default)))
                .Subscribe(
                    _ =>
                    {
                        if (!initializing)
                        {
                            observer.OnNext(Unit.Default);
                        }
                    },
                    observer.OnError);

            // 先完成集合及条目订阅，再挂载，避免漏掉挂载回调中的配置变化。
            initializing = false;
            observer.OnNext(Unit.Default);
            return subscription;
        });
    }

    private void StopObserving()
    {
        var subscription = _subscription;
        _subscription = null;
        subscription?.Dispose();
    }

    private void RequestUpdate()
    {
        _updateRequested = true;
        if (_isUpdating)
        {
            return;
        }

        _isUpdating = true;
        try
        {
            while (_updateRequested)
            {
                _updateRequested = false;
                UnloadFeatures();
                if (!_isAttached || _isDisposed || _updateRequested)
                {
                    continue;
                }

                var features = _featureContainer?.Features.ToArray() ?? [];
                foreach (var settings in features)
                {
                    if (!_isAttached || _isDisposed || _updateRequested)
                    {
                        break;
                    }

                    AttachFeature(settings);
                }
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void AttachFeature(FeatureInfo settings)
    {
        var id = settings.Id;
        var registration = _registry.Find(id);
        if (registration == null)
        {
            _logger.LogWarning("未注册特性：{FeatureId}", id);
            return;
        }

        // 声明的目标类型可以是宿主运行时类型的基类或其实现的接口。
        if (!registration.TargetType.IsInstanceOfType(_host))
        {
            return;
        }

        FeatureBase? feature = null;
        try
        {
            _initializingFeature = settings;
            try
            {
                feature = _featureService.CreateFeature(settings);
            }
            finally
            {
                _initializingFeature = null;
            }

            if (feature == null)
            {
                return;
            }

            if (_isAttached && !_isDisposed && !_updateRequested)
            {
                feature.AttachTo(_host);
                if (_isAttached && !_isDisposed && !_updateRequested)
                {
                    _attachedFeatures.Add((id, feature));
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "无法创建或挂载特性：{FeatureId}", id);
        }

        if (feature != null)
        {
            DisposeFeature(id, feature);
        }
    }

    private void UnloadFeatures()
    {
        var features = _attachedFeatures.ToArray();
        _attachedFeatures.Clear();
        for (var index = features.Length - 1; index >= 0; index--)
        {
            DisposeFeature(features[index].Id, features[index].Feature);
        }
    }

    private void DisposeFeature(string id, FeatureBase feature)
    {
        try
        {
            feature.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "无法卸载特性：{FeatureId}", id);
        }
    }
}
