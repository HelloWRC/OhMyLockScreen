using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Models;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models;

namespace OhMyLockScreen.Services;

public sealed class SettingsService(ILogger<SettingsService> logger, string settingsPath)
    : ObservableObject, IDisposable
{
    private readonly object _sync = new();
    private Settings _settings = new();
    private bool _isLoaded;
    private bool _isDisposed;

    public Settings Settings
    {
        get => _settings;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                if (ReferenceEquals(_settings, value)) return;
                _settings.PropertyChanged -= OnSettingsChanged;
                _settings = value;
                if (_isLoaded) _settings.PropertyChanged += OnSettingsChanged;
                OnPropertyChanged();
            }
        }
    }

    public Task LoadSettingsAsync()
    {
        ReloadSettings();
        return Task.CompletedTask;
    }

    public void ReloadSettings()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _settings.PropertyChanged -= OnSettingsChanged;
            _isLoaded = false;
            try
            {
                Settings = ConfigureFileHelper.LoadConfig<Settings>(settingsPath);
            }
            finally
            {
                _isLoaded = true;
                _settings.PropertyChanged += OnSettingsChanged;
            }
        }
    }

    public void SaveSettings(string note = "")
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            logger.LogInformation("写入应用设置：{Note}", note);
            ConfigureFileHelper.SaveConfig(settingsPath, _settings);
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        lock (_sync)
        {
            if (!_isLoaded || _isDisposed || !ReferenceEquals(sender, _settings)) return;
            try
            {
                SaveSettings(e.PropertyName ?? "");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "自动保存应用设置失败");
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed) return;
            _settings.PropertyChanged -= OnSettingsChanged;
            _isDisposed = true;
        }
    }
}
