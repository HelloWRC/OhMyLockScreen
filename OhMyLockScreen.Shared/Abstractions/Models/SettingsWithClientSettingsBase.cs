using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;

namespace OhMyLockScreen.Shared.Abstractions.Models;

public abstract partial class SettingsWithClientSettingsBase<TSettings> : ObservableObject
{
    [ObservableProperty] private TSettings? _settings;
    
    public T? TryGetSettings<T>() where T : class, TSettings => Settings as T;

    public T GetSettings<T>() where T : class, TSettings => TryGetSettings<T>() ?? throw new NullReferenceException();

    public T GetSettingsOrCreate<T>() where T : class, TSettings
    {
        var settings = TryGetSettings<T>();
        if (settings != null)
        {
            return settings;
        }

        Settings = settings = Activator.CreateInstance<T>();
        return settings;
    }

    public TSettings GetSettingsOrCreate(Type type)
    {
        if (Settings != null && type.IsInstanceOfType(Settings))
        {
            return Settings;
        }

        Settings = (TSettings)Activator.CreateInstance(type)!;
        return Settings;
    }
} 