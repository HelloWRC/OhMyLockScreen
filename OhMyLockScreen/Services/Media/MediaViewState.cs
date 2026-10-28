using System.ComponentModel;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Models.Media;
using Windows.UI.Core;

namespace OhMyLockScreen.Services.Media;

/// <summary>每个呈现实例只在自己的 UI 线程读取有效状态，不更改单例的真实状态。</summary>
public sealed class MediaViewState(IMediaStateService service, CoreDispatcher dispatcher) : IDisposable
{
    private bool _active;
    private int _generation;
    public bool IsEditing { get; set; }
    public bool IsPlaceholder => IsEditing && !service.State.HasSession;
    public MediaState State => IsPlaceholder ? service.Placeholder : service.State;
    public event EventHandler? Changed;

    public void Activate()
    {
        if (!_active)
        {
            _active = true;
            ++_generation;
            service.PropertyChanged += OnStateChanged;
        }
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs args) => RequestRefresh();

    public async void RequestRefresh()
    {
        var generation = _generation;
        try
        {
            if (dispatcher.HasThreadAccess)
            {
                Notify();
            }
            else
            {
                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, Notify);
            }
        }
        catch (Exception exception)
        {
            IAppHost.TryGetService<ILogger<MediaViewState>>()?.LogDebug(exception, "媒体视图已关闭，无法调度更新");
        }

        void Notify()
        {
            if (_active && generation == _generation)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Dispose()
    {
        _active = false;
        ++_generation;
        service.PropertyChanged -= OnStateChanged;
    }
}
