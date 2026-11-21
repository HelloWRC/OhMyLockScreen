using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Services;
using Windows.ApplicationModel.Core;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;

namespace OhMyLockScreen;

public sealed partial class App
{
    private readonly Lazy<Task> _initializeHost;
    private readonly Queue<Exception> _earlyUnhandledExceptions = new();
    private readonly object _exitSync = new();
    private IHost? _host;
    private Task? _exitTask;
    private volatile bool _isExiting;
    private async Task StartHostAsync(Func<IHost> hostFactory)
    {
        _host = hostFactory();
        IAppHost.Host = _host;
        try
        {
            lock (_earlyUnhandledExceptions)
            {
                while (_earlyUnhandledExceptions.TryDequeue(out var exception))
                {
                    _host.Services.GetService<ILogger<App>>()?.LogCritical(exception, UnhandledExceptionToastTitle);
                }
            }
            await _host.Services.GetRequiredService<SettingsService>().LoadSettingsAsync();
            _host.Services.GetService<ISceneService>()?.Initialize();
            await _host.StartAsync();
        }
        catch
        {
            DisposeHost();
            throw;
        }
    }

    internal Task ExitAsync()
    {
        lock (_exitSync)
        {
            return _exitTask ??= ExitCoreAsync();
        }
    }

    private async Task ExitCoreAsync()
    {
        _isExiting = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            if (_initializeHost.IsValueCreated)
            {
                await _initializeHost.Value.WaitAsync(timeout.Token);
            }

            SaveSettingsSafely("应用退出");
            if (_host != null)
            {
                // WaitAsync also bounds hosted services that ignore cancellation.
                await _host.StopAsync(timeout.Token).WaitAsync(timeout.Token);
            }
        }
        catch (Exception exception)
        {
            LogLifecycleError(exception, "停止应用主机失败");
        }
        finally
        {
            DisposeHost();
            CoreApplication.Exit();
        }
    }

    private void SaveSettingsSafely(string note)
    {
        try
        {
            _host?.Services.GetService<ISceneService>()?.FlushPending();
        }
        catch (Exception exception)
        {
            LogLifecycleError(exception, "保存场景失败");
        }

        try
        {
            _host?.Services.GetService<SettingsService>()?.SaveSettings(note);
        }
        catch (Exception exception)
        {
            LogLifecycleError(exception, "保存应用设置失败");
        }
    }

    private void DisposeHost()
    {
        var currentHost = _host;
        _host = null;
        if (ReferenceEquals(IAppHost.Host, currentHost))
        {
            IAppHost.Host = null;
        }

        try
        {
            currentHost?.Dispose();
        }
        catch (Exception exception)
        {
            LogLifecycleError(exception, "释放应用主机失败");
        }
    }

    private void LogUnhandledException(Exception exception)
    {
        ILogger<App>? logger;
        lock (_earlyUnhandledExceptions)
        {
            if (_host == null && !_isExiting)
            {
                // XAML can report an exception before the first activation creates the host.
                _earlyUnhandledExceptions.Enqueue(exception);
                Debug.WriteLine($"{UnhandledExceptionToastTitle}: {exception}");
                return;
            }
            logger = _host?.Services.GetService<ILogger<App>>();
        }

        logger?.LogCritical(exception, UnhandledExceptionToastTitle);
    }

    private void LogLifecycleError(Exception exception, string message)
    {
        Debug.WriteLine($"{message}: {exception}");
        _host?.Services.GetService<ILogger<App>>()?.LogError(exception, "{Message}", message);
    }
}