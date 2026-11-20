using OhMyLockScreen.Shared.Abstractions.Services;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.Ruleset;
using Windows.UI.Core;

namespace OhMyLockScreen.Shared.Services;

/// <summary>在宿主视图上判定规则并更新三级状态；后台通知合并到下一次 UI 调度。</summary>
public sealed class RulesetObservation : IDisposable
{
    private readonly IRulesetService _service;
    private readonly Ruleset _ruleset;
    private readonly CoreDispatcher _dispatcher;
    private readonly Action<bool>? _updated;
    private readonly ObservableSettingsGraph _graph;
    private int _pending;
    private volatile bool _disposed;

    public RulesetObservation(IRulesetService service, Ruleset ruleset, CoreDispatcher dispatcher, Action<bool>? updated = null)
    {
        (_service, _ruleset, _dispatcher, _updated) = (service, ruleset, dispatcher, updated);
        _graph = new(ruleset, (_, _, _) => Schedule());
        service.StatusUpdated += OnStatusUpdated;
        Evaluate();
    }

    private void OnStatusUpdated(object? sender, EventArgs args) => Schedule();

    private void Schedule()
    {
        if (_disposed || Interlocked.Exchange(ref _pending, 1) != 0) return;
        _ = _dispatcher.TryRunAsync(CoreDispatcherPriority.Normal, () =>
        {
            Interlocked.Exchange(ref _pending, 0);
            if (!_disposed) Evaluate();
        });
    }

    private void Evaluate()
    {
        var satisfied = _service.IsRulesetSatisfied(_ruleset);
        _updated?.Invoke(satisfied);
    }

    public void Dispose()
    {
        _disposed = true;
        _service.StatusUpdated -= OnStatusUpdated;
        _graph.Dispose();
    }
}
