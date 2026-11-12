using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Hosting;
using OhMyLockScreen.Models;
using OhMyLockScreen.Services;
using OhMyLockScreen.Shared.Extensions.Registry;
using OhMyLockScreen.Shared.Services.Registry;
using OhMyLockScreen.Views;
using OhMyLockScreen.Views.SettingsPages;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.Devices.Input;

internal static class InfrastructureTests
{
    private static string SettingsPath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "Settings.json");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    internal static async Task RunAsync(Action<string, Action> run, Func<string, Func<Task>, Task> runAsync)
    {
        run("Missing configuration creates defaults and supports JSON round trips", () =>
        {
            var config = ConfigureFileHelper.LoadConfigUnWrapped<SampleConfig>(SettingsPath);
            Check(config.Value == 7 && File.Exists(SettingsPath), "Default config was not created.");
            Check(!File.Exists(SettingsPath + ".bak"), "Saving unexpectedly created a backup.");
            config.Value = 42;
            config.Items.Add("one");
            ConfigureFileHelper.SaveConfig(SettingsPath, config, true);
            Check(File.ReadAllText(SettingsPath).Contains('\n'), "Indented output was not formatted.");
            var loaded = ConfigureFileHelper.LoadConfig<SampleConfig>(SettingsPath);
            Check(loaded.Value == 42 && loaded.Items.SequenceEqual(["one"]), "Config did not round trip.");
            var backup = File.ReadAllText(SettingsPath + ".bak");
            ConfigureFileHelper.SaveConfig(SettingsPath, new SampleConfig { Value = 99 });
            Check(File.ReadAllText(SettingsPath + ".bak") == backup, "Saving changed the last loaded backup.");
        });

        run("Valid backup is checked and restored before reporting recovery", () =>
        {
            ConfigureFileHelper.SaveConfig(SettingsPath, new SampleConfig { Value = 31 });
            ConfigureFileHelper.LoadConfig<SampleConfig>(SettingsPath);
            var backup = File.ReadAllText(SettingsPath + ".bak");
            File.WriteAllText(SettingsPath, "invalid main");
            var loaded = ConfigureFileHelper.LoadConfigUnWrapped<SampleConfig>(SettingsPath);
            Check(loaded.Value == 31 && File.ReadAllText(SettingsPath) == backup, "Backup was not restored.");
            Check(ConfigureFileHelper.Errors is [{ Level: 0, Critical: false }], "Recovery error was not recorded.");
        });

        run("Two corrupt configs return defaults without restoring corrupt backup bytes", () =>
        {
            File.WriteAllText(SettingsPath, "invalid main");
            File.WriteAllText(SettingsPath + ".bak", "invalid backup");
            var loaded = ConfigureFileHelper.LoadConfig<SampleConfig>(SettingsPath);
            Check(loaded.Value == 7, "Wrapped load did not return defaults.");
            Check(File.ReadAllText(SettingsPath) == "invalid main" && File.ReadAllText(SettingsPath + ".bak") == "invalid backup",
                "Failed recovery overwrote evidence.");
            Check(ConfigureFileHelper.Errors is [{ Level: 1, Critical: true }], "Fatal error was not recorded.");
            Throws<JsonException>(() => ConfigureFileHelper.LoadConfigUnWrapped<SampleConfig>(SettingsPath));
        });

        run("Backup policy supports global defaults and per-call overrides", () =>
        {
            ConfigureFileHelper.SaveConfig(SettingsPath, new SampleConfig());
            ConfigureFileHelper.IsBackupEnabled = false;
            ConfigureFileHelper.LoadConfigUnWrapped<SampleConfig>(SettingsPath);
            Check(!File.Exists(SettingsPath + ".bak"), "Global backup disable was ignored.");
            ConfigureFileHelper.LoadConfigUnWrapped<SampleConfig>(SettingsPath, true);
            Check(File.Exists(SettingsPath + ".bak"), "Per-call backup enable was ignored.");
            File.WriteAllText(SettingsPath, "invalid");
            Throws<JsonException>(() => ConfigureFileHelper.LoadConfigUnWrapped<SampleConfig>(SettingsPath, false));
            Check(ConfigureFileHelper.LoadConfig<SampleConfig>(SettingsPath, true).Value == 7, "Per-call recovery failed.");
            Throws<FileNotFoundException>(() => ConfigureFileHelper.LoadConfigUnWrapped<SampleConfig>(SettingsPath + ".missing", false, true));
        });

        run("A failed backup write never restores stale data over a valid main file", () =>
        {
            ConfigureFileHelper.SaveConfig(SettingsPath, new SampleConfig { Value = 51 });
            Directory.CreateDirectory(SettingsPath + ".bak");
            Throws<IOException>(() => ConfigureFileHelper.LoadConfigUnWrapped<SampleConfig>(SettingsPath));
            Check(ConfigureFileHelper.LoadConfigUnWrapped<SampleConfig>(SettingsPath, false).Value == 51,
                "Backup write failure replaced the valid main config.");
        });

        run("Stream overloads preserve caller ownership on success and failure", () =>
        {
            using var stream = new MemoryStream();
            ConfigureFileHelper.SaveConfig(stream, new SampleConfig { Value = 81 }, true);
            Check(stream.CanWrite && Encoding.UTF8.GetString(stream.ToArray()).Contains('\n'), "Stream save closed or ignored formatting.");
            stream.Position = 0;
            Check(ConfigureFileHelper.LoadConfigUnWrapped<SampleConfig>(stream).Value == 81 && stream.CanRead, "Stream load closed the stream.");
            using var invalid = new MemoryStream(Encoding.UTF8.GetBytes("invalid"));
            Throws<JsonException>(() => ConfigureFileHelper.LoadConfigUnWrapped<SampleConfig>(invalid));
            Check(invalid.CanRead, "Failed unwrapped load closed the stream.");
            invalid.Position = 0;
            Check(ConfigureFileHelper.LoadConfig<SampleConfig>(invalid).Value == 7 && invalid.CanRead, "Wrapped stream fallback failed.");
            Check(ConfigureFileHelper.Errors is [{ Path: "<stream>", Level: 1 }], "Stream error was not recorded.");
            using var readOnly = new MemoryStream([], writable: false);
            Throws<ArgumentException>(() => ConfigureFileHelper.SaveConfig(readOnly, new SampleConfig()));
            Check(readOnly.CanRead, "Failed stream save closed the caller's stream.");
        });

        run("JSON deep copy isolates nested collections and leaves global defaults alone", () =>
        {
            var defaults = JsonSerializerOptions.Default;
            var config = new SampleConfig { Items = ["original"] };
            var copy = ConfigureFileHelper.CopyObject(config);
            copy.Items.Add("copy");
            Check(!ReferenceEquals(config, copy) && config.Items.Count == 1, "Copy shares nested state.");
            Check(ReferenceEquals(defaults, JsonSerializerOptions.Default), "Configuration API replaced global JSON defaults.");
        });

        run("File write failures are observable to callers", () =>
        {
            var blockedPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "directory");
            Directory.CreateDirectory(blockedPath);
            Throws<UnauthorizedAccessException>(() => ConfigureFileHelper.SaveConfig(blockedPath, new SampleConfig()));
            Check(ConfigureFileHelper.LoadConfig<SampleConfig>(blockedPath).Value == 7 && ConfigureFileHelper.Errors.Count == 1,
                "Wrapped file creation failure did not record its fallback.");
        });

        run("Settings auto-save once after load and detach replaced or reloaded models", () =>
        {
            ConfigureFileHelper.SaveConfig(SettingsPath, new Settings { TestValue = 11 });
            var logger = new RecordingLogger<SettingsService>();
            using var service = new SettingsService(logger, SettingsPath);
            service.LoadSettingsAsync().GetAwaiter().GetResult();
            Check(service.Settings.TestValue == 11 && logger.Saves == 0, "Loading triggered an auto-save.");
            service.Settings.TestValue = 12;
            Check(logger.Saves == 1 && ReadTestValue() == 12, "Property change was not saved exactly once.");
            var old = service.Settings;
            service.LoadSettingsAsync().GetAwaiter().GetResult();
            service.LoadSettingsAsync().GetAwaiter().GetResult();
            old.TestValue = 99;
            Check(logger.Saves == 1, "Reload left the old model subscribed.");
            service.Settings.TestValue = 13;
            Check(logger.Saves == 2, "Repeated loading duplicated subscriptions.");
            old = service.Settings;
            service.Settings = new Settings();
            old.TestValue = 100;
            service.Settings.TestValue = 14;
            Check(logger.Saves == 3 && ReadTestValue() == 14, "Replacement did not transfer the subscription.");
            service.SaveSettings("explicit");
            Check(logger.Saves == 4, "Explicit save did not write settings.");
            service.Dispose();
            service.Settings.TestValue = 15;
            Check(logger.Saves == 4 && ReadTestValue() == 14, "Disposal left auto-save active.");
            Throws<ObjectDisposedException>(() => service.SaveSettings());
        });

        run("Settings auto-save logs failures while explicit save throws", () =>
        {
            var logger = new RecordingLogger<SettingsService>();
            using var service = new SettingsService(logger, ApplicationData.Current.LocalFolder.Path);
            service.LoadSettingsAsync().GetAwaiter().GetResult();
            service.Settings.TestValue = 2;
            Check(logger.Errors == 1 && service.Settings.TestValue == 2, "Auto-save failure escaped or discarded memory state.");
            Throws<UnauthorizedAccessException>(() => service.SaveSettings());
        });

        await runAsync("Production host registers settings at the UWP local folder", async () =>
        {
            Throws<InvalidOperationException>(() => IAppHost.GetRequiredService<SettingsService>());
            Check(IAppHost.TryGetService<SettingsService>() == null, "Optional access before startup must return null.");
            var app = new App();
            var view = new ViewContext();
            view.Invoke(() => app.Launch(new LaunchActivatedEventArgs()));
            var stopwatch = Stopwatch.StartNew();
            while (view.Window.Content == null)
            {
                Check(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Production startup did not return to its view.");
                view.Drain();
                await Task.Delay(5);
            }

            Check(view.Window.Content is WindowToastHost && File.Exists(SettingsPath), "Production startup did not load local settings.");
            Check(IAppHost.GetRequiredService<IHostLifetime>() is UwpHostLifetime, "Host used a console lifetime.");
            var service = IAppHost.GetRequiredService<SettingsService>();
            service.Settings.TestValue = 21;
            Check(ReadTestValue() == 21, "Registered settings were not persisted.");
            await app.ExitAsync();
            Check(IAppHost.Host == null && CoreApplication.ExitCalls == 1, "Exit did not clear the host.");
        });

        await runAsync("Repeated activation and suspension reuse one started host", async () =>
        {
            var probe = new HostedProbe();
            int builds = 0;
            var app = new App(() => { builds++; return BuildHost(probe); });
            app.Launch(new LaunchActivatedEventArgs());
            var firstHost = IAppHost.Host;
            var firstWindow = Window.Current;
            Window.Current = new Window();
            app.Activate(new ActivatedEventArgs(ActivationKind.LockScreen));
            Check(Window.Current.Content is WindowToastHost && firstWindow.Content is WindowToastHost, "Activation lost one of the views.");
            File.Delete(SettingsPath);
            var suspend = app.Suspend();
            Check(suspend.SuspendingOperation.Deferral.IsComplete && File.Exists(SettingsPath), "Suspend did not save and complete its deferral.");
            app.Activate(new ActivatedEventArgs(ActivationKind.LockScreenComponent));
            Check(builds == 1 && probe.Starts == 1 && probe.Stops == 0 && ReferenceEquals(firstHost, IAppHost.Host),
                "Suspend or reactivation restarted the host.");
            await app.ExitAsync();
            Check(probe.Stops == 1 && probe.Disposed && IAppHost.Host == null, "Exit did not stop and dispose the host.");
        });

        await runAsync("Desktop activation restores the system title bar and retains settings on reactivation", async () =>
        {
            LockScreenHosting.IsHosted = () => false;
            var app = new App(() => BuildHost(new HostedProbe()));
            var titleBar = CoreApplication.GetCurrentView().TitleBar;
            titleBar.ExtendViewIntoTitleBar = true;
            app.Launch(new LaunchActivatedEventArgs());
            var root = ((WindowToastHost)Window.Current.Content!).Frame;
            Check(root.Content is SceneEditorPage, "Desktop launch did not open the editor.");
            Check(!titleBar.ExtendViewIntoTitleBar, "Desktop launch kept the persisted custom title bar layout.");
            Check(root.Navigate(typeof(ApplicationSettingsPage)), "Could not navigate to settings.");
            var settings = root.Content;

            app.Launch(new LaunchActivatedEventArgs());
            app.Activate(new ActivatedEventArgs(ActivationKind.Other));
            Check(ReferenceEquals(root.Content, settings), "Desktop activation replaced the current settings page.");

            titleBar.ExtendViewIntoTitleBar = true;
            app.Activate(new ActivatedEventArgs(ActivationKind.LockScreen));
            Check(root.Content is MainPage, "Lock-screen activation kept the desktop settings page.");
            Check(titleBar.ExtendViewIntoTitleBar, "Lock-screen activation changed the host's title bar layout.");
            await app.ExitAsync();
        });

        run("Settings registration sorts by category and rejects duplicate IDs per host", () =>
        {
            var services = new ServiceCollection();
            services.AddSettingsPage<AboutSettingsPage>();
            services.AddSettingsPage<GeneralSettingsPage>();
            Throws<ArgumentException>(() => services.AddSettingsPage<GeneralSettingsPage>());
            using var host = services.BuildServiceProvider();
            var registry = host.GetRequiredService<SettingsPageRegistry>();
            Check(registry.Pages.Select(page => page.Info.Id).SequenceEqual(["general", "about"]),
                "Settings registration did not sort by category.");
            Check(registry.Find("general")?.PageType == typeof(GeneralSettingsPage),
                "General settings did not resolve to its Frame page type.");

            using var secondHost = new ServiceCollection().AddSettingsPage<AboutSettingsPage>().BuildServiceProvider();
            Check(secondHost.GetRequiredService<SettingsPageRegistry>().Pages.Count == 1,
                "Page metadata leaked between hosts.");
        });

        run("Settings registration reads independent title width and visibility markers", () =>
        {
            using var host = new ServiceCollection()
                .AddSettingsPage<GeneralSettingsPage>()
                .AddSettingsPage<WideTitleSettingsPage>()
                .AddSettingsPage<HiddenTitleSettingsPage>()
                .BuildServiceProvider();
            var registry = host.GetRequiredService<SettingsPageRegistry>();
            Check(registry.Find(typeof(GeneralSettingsPage))?.Info is { UseFullWidth: false, HidePageTitle: false },
                "Unmarked pages did not keep the default title layout.");
            Check(registry.Find(typeof(WideTitleSettingsPage))?.Info is { UseFullWidth: true, HidePageTitle: false },
                "FullWidthPage did not apply independently.");
            Check(registry.Find(typeof(HiddenTitleSettingsPage))?.Info is { UseFullWidth: false, HidePageTitle: true },
                "HidePageTitle did not apply independently.");
        });

        await runAsync("Delayed startup shares one task and returns to each owning view", async () =>
        {
            var probe = new HostedProbe { StartGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var app = new App(() => BuildHost(probe));
            LockScreenHosting.IsHosted = () => false;
            var firstView = new ViewContext();
            var secondView = new ViewContext();
            firstView.Invoke(() => app.Launch(new LaunchActivatedEventArgs()));
            firstView.Invoke(() => app.Activate(new ActivatedEventArgs(ActivationKind.LockScreen)));
            secondView.Invoke(() => app.Activate(new ActivatedEventArgs(ActivationKind.LockScreenComponent)));
            firstView.Invoke(() => app.Activate(new ActivatedEventArgs(ActivationKind.Other)));
            Check(firstView.Window.Content == null && secondView.Window.Content == null && probe.Starts == 1,
                "Views appeared before shared startup completed.");
            Check(app.Suspend().SuspendingOperation.Deferral.IsComplete, "Suspension waited for hosted startup.");
            probe.StartGate.SetResult();
            // Each completed stage of Host.StartAsync may post another UI continuation.
            // Pump both views as real UI event loops do, until activation is complete.
            var stopwatch = Stopwatch.StartNew();
            while (firstView.Window.Content == null || secondView.Window.Content == null)
            {
                Check(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Activation did not return to its UI context.");
                firstView.Drain();
                secondView.Drain();
                await Task.Delay(10);
            }
            Check(firstView.Window.Content is WindowToastHost && secondView.Window.Content is WindowToastHost, "An asynchronous activation lost its view.");
            Check(firstView.Window.Content is WindowToastHost { Frame.Content: SceneEditorPage } &&
                secondView.Window.Content is WindowToastHost { Frame.Content: MainPage },
                "Delayed startup lost the latest activation kind or shared it across views.");
            await app.ExitAsync();
        });

        await runAsync("Activation without a synchronization context dispatches each window and preserves its latest role", async () =>
        {
            var probe = new HostedProbe { StartGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var builds = 0;
            var app = new App(() => { builds++; return BuildHost(probe); });
            LockScreenHosting.IsHosted = () => false;
            var first = new ViewContext(useSynchronizationContext: false);
            var second = new ViewContext(useSynchronizationContext: false);
            first.Window.EnforceThreadAccess = true;
            second.Window.EnforceThreadAccess = true;
            first.Invoke(() => app.Launch(new LaunchActivatedEventArgs()));
            first.Invoke(() => app.Activate(new ActivatedEventArgs(ActivationKind.LockScreen)));
            second.Invoke(() => app.Activate(new ActivatedEventArgs(ActivationKind.LockScreenComponent)));
            probe.StartGate.SetResult();
            await WaitForDispatchAsync(first.Window.Dispatcher);
            await WaitForDispatchAsync(second.Window.Dispatcher);
            first.Invoke(() => Check(first.Window.Content == null, "A continuation accessed the first window before dispatch."));
            second.Invoke(() => Check(second.Window.Content == null, "A continuation accessed the second window before dispatch."));

            // A newer activation can arrive while older continuations are queued.
            first.Invoke(() => app.Activate(new ActivatedEventArgs(ActivationKind.Other)));
            first.Drain();
            first.Invoke(() => Check(first.Window.Content is WindowToastHost { Frame.Content: SceneEditorPage },
                "A stale queued activation replaced the latest page role."));
            second.Invoke(() => Check(second.Window.Content == null, "The first view drained another view's activation."));
            second.Drain();
            second.Invoke(() => Check(second.Window.Content is WindowToastHost { Frame.Content: MainPage },
                "The lock-screen view did not receive its own page."));
            Check(first.Window.WrongThreadContentAccesses == 0 && second.Window.WrongThreadContentAccesses == 0,
                "Window.Content was accessed outside its owning UI view.");
            Check(builds == 1 && probe.Starts == 1, "Views stopped sharing one initialized host.");
            await app.ExitAsync();
        });

        await runAsync("Closing a view while activation is queued skips its page without blocking another view", async () =>
        {
            var probe = new HostedProbe { StartGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var app = new App(() => BuildHost(probe));
            var closed = new ViewContext(useSynchronizationContext: false);
            var active = new ViewContext(useSynchronizationContext: false);
            closed.Window.EnforceThreadAccess = true;
            active.Window.EnforceThreadAccess = true;
            closed.Invoke(() => app.Activate(new ActivatedEventArgs(ActivationKind.LockScreen)));
            active.Invoke(() => app.Activate(new ActivatedEventArgs(ActivationKind.LockScreen)));
            probe.StartGate.SetResult();
            await WaitForDispatchAsync(closed.Window.Dispatcher);
            await WaitForDispatchAsync(active.Window.Dispatcher);
            closed.Invoke(closed.Window.Close);
            closed.Drain();
            active.Drain();
            closed.Invoke(() => Check(closed.Window.Content == null, "A queued callback created a page after window closure."));
            active.Invoke(() => Check(active.Window.Content is WindowToastHost { Frame.Content: MainPage },
                "Closing another view suppressed the active lock-screen window."));
            await app.ExitAsync();
        });

        await runAsync("Exit after activation dispatch suppresses queued page creation", async () =>
        {
            var probe = new HostedProbe { StartGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var app = new App(() => BuildHost(probe));
            var view = new ViewContext(useSynchronizationContext: false);
            view.Window.EnforceThreadAccess = true;
            view.Invoke(() => app.Activate(new ActivatedEventArgs(ActivationKind.LockScreen)));
            probe.StartGate.SetResult();
            await WaitForDispatchAsync(view.Window.Dispatcher);
            await app.ExitAsync();
            view.Drain();
            view.Invoke(() => Check(view.Window.Content == null, "A queued activation displayed a page after application exit."));
            Check(probe.Disposed && CoreApplication.ExitCalls == 1, "Queued activation prevented host cleanup.");
        });

        run("Page display failures are logged and do not escape async activation", () =>
        {
            var logger = new RecordingLogger<App>();
            var app = new App(() => BuildHost(new HostedProbe(), appLogger: logger));
            var window = Window.Current;
            window.ContentAccessException = new System.Runtime.InteropServices.COMException("Simulated window failure.");
            app.Activate(new ActivatedEventArgs(ActivationKind.LockScreen));
            Check(logger.Errors == 1, "Page display failure escaped or was not logged.");
            window.ContentAccessException = null;
            app.Activate(new ActivatedEventArgs(ActivationKind.LockScreen));
            Check(window.Content is WindowToastHost { Frame.Content: MainPage }, "Activation did not recover after a display failure.");
        });

        await runAsync("Exit during startup waits for cleanup without showing a page", async () =>
        {
            var probe = new HostedProbe { StartGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var app = new App(() => BuildHost(probe));
            var window = Window.Current;
            app.Launch(new LaunchActivatedEventArgs());
            var exit = app.ExitAsync();
            Check(!exit.IsCompleted && window.Content == null, "Exit ignored pending startup.");
            probe.StartGate.SetResult();
            await exit;
            Check(window.Content == null && probe.Stops == 1 && probe.Disposed && CoreApplication.ExitCalls == 1,
                "Startup displayed a page or leaked the host after exit began.");
        });

        await runAsync("Animated exit cleans the page and merges overlapping shutdown requests", async () =>
        {
            var probe = new HostedProbe { StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var app = new App(() => BuildHost(probe));
            app.Launch(new LaunchActivatedEventArgs());
            var page = (MainPage)((WindowToastHost)Window.Current.Content!).Frame.Content!;
            page.RootScenePresenter.ActualHeight = 720;
            page.Load();
            page.PressPointer(PointerDeviceType.Mouse);
            Check(probe.Stops == 0 && CoreApplication.ExitCalls == 0, "Shutdown started before the animation finished.");
            Storyboard.LastCreated!.Advance(TimeSpan.FromMilliseconds(333));
            var firstExit = app.ExitAsync();
            var secondExit = app.ExitAsync();
            Check(ReferenceEquals(firstExit, secondExit) && probe.Stops == 1 && !firstExit.IsCompleted, "Shutdown was not coalesced.");
            Check(page.KeySubscribers == 0 && page.PointerSubscribers == 0 &&
                Window.Current.CoreWindow.KeySubscribers == 0 &&
                Window.Current.CoreWindow.PointerSubscribers == 0 &&
                Window.Current.CoreWindow.Dispatcher.AcceleratorSubscribers == 0,
                "Page tracking continued while shutdown was pending.");
            Check(page.RootScenePresenter.LayersLayerElement.Opacity == 0 &&
                page.RootScenePresenter.LayersTranslation.Y == -720,
                "Shutdown restored the scene layers after the unlock animation.");
            probe.StopGate.SetResult();
            await firstExit;
            Check(CoreApplication.ExitCalls == 1 && probe.Disposed, "Overlapping exit requests exited more than once.");
        });

        await runAsync("Suspend and exit tolerate settings write failures", async () =>
        {
            var probe = new HostedProbe();
            var logger = new RecordingLogger<SettingsService>();
            var app = new App(() => BuildHost(probe, ApplicationData.Current.LocalFolder.Path, logger));
            app.Launch(new LaunchActivatedEventArgs());
            Check(app.Suspend().SuspendingOperation.Deferral.IsComplete, "Save failure leaked the suspension deferral.");
            await app.ExitAsync();
            Check(probe.Stops == 1 && probe.Disposed && CoreApplication.ExitCalls == 1, "Save failure prevented orderly exit.");
        });

        await runAsync("Hosted stop failure still disposes and exits", async () =>
        {
            var probe = new HostedProbe { ThrowOnStop = true };
            var app = new App(() => BuildHost(probe));
            app.Launch(new LaunchActivatedEventArgs());
            await app.ExitAsync();
            Check(probe.Stops == 1 && probe.Disposed && CoreApplication.ExitCalls == 1 && IAppHost.Host == null,
                "Stop failure prevented disposal or exit.");
        });

        await runAsync("Uncooperative hosted shutdown is bounded to five seconds", async () =>
        {
            var probe = new HostedProbe { StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var app = new App(() => BuildHost(probe));
            app.Launch(new LaunchActivatedEventArgs());
            var stopwatch = Stopwatch.StartNew();
            await app.ExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Check(stopwatch.Elapsed >= TimeSpan.FromSeconds(4.5) && probe.Disposed && CoreApplication.ExitCalls == 1,
                "Stop timeout failed to release the host and exit.");
            probe.StopGate.SetResult();
        });

        await runAsync("Failed startup releases the host and completes suspension deferrals", async () =>
        {
            var probe = new HostedProbe { ThrowOnStart = true };
            var app = new App(() => BuildHost(probe));
            app.Launch(new LaunchActivatedEventArgs());
            Check(Window.Current.Content == null && probe.Disposed && IAppHost.Host == null, "Startup failure leaked a host or showed an uninitialized page.");
            Check(app.Suspend().SuspendingOperation.Deferral.IsComplete, "Failed startup leaked a deferral.");
            await app.ExitAsync();
            Check(CoreApplication.ExitCalls == 1, "Failed startup blocked Exit.");
        });
    }

    private static int ReadTestValue() => ConfigureFileHelper.LoadConfigUnWrapped<Settings>(SettingsPath, false).TestValue;

    private static async Task WaitForDispatchAsync(Windows.UI.Core.CoreDispatcher dispatcher)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (dispatcher.PendingCallbacks == 0)
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private static IHost BuildHost(HostedProbe probe, string? path = null, ILogger<SettingsService>? logger = null,
        ILogger<App>? appLogger = null)
    {
        return new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IHostLifetime, UwpHostLifetime>();
                services.AddSingleton(provider => new SettingsService(logger ?? provider.GetRequiredService<ILogger<SettingsService>>(), path ?? SettingsPath));
                services.AddSingleton<IHostedService>(_ => probe);
                if (appLogger != null)
                {
                    services.AddSingleton(appLogger);
                }
            })
            .Build();
    }

    public sealed class SampleConfig
    {
        public int Value { get; set; } = 7;
        public List<string> Items { get; set; } = [];
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public int Saves { get; private set; }
        public int Errors { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information && formatter(state, exception).StartsWith("写入应用设置：")) Saves++;
            if (logLevel == LogLevel.Error) Errors++;
        }
    }

    private sealed class HostedProbe : IHostedService, IDisposable
    {
        public TaskCompletionSource? StartGate { get; init; }
        public TaskCompletionSource? StopGate { get; init; }
        public bool ThrowOnStart { get; init; }
        public bool ThrowOnStop { get; init; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public bool Disposed { get; private set; }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Starts++;
            if (ThrowOnStart) throw new InvalidOperationException("Simulated startup failure.");
            if (StartGate != null) await StartGate.Task.WaitAsync(cancellationToken);
        }
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Stops++;
            if (ThrowOnStop) throw new InvalidOperationException("Simulated shutdown failure.");
            if (StopGate != null) await StopGate.Task;
        }
        public void Dispose() => Disposed = true;
    }

    internal sealed class ViewContext(bool useSynchronizationContext = true) : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        internal Window Window { get; } = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            callbacks.Enqueue((d, state));
        }

        internal void Invoke(Action action)
        {
            var previous = Current;
            var previousWindow = Window.Current;
            SetSynchronizationContext(useSynchronizationContext ? this : null);
            Window.Current = Window;
            try { action(); }
            finally
            {
                SetSynchronizationContext(previous);
                Window.Current = previousWindow;
            }
        }

        internal void Drain()
        {
            Invoke(() =>
            {
                while (callbacks.TryDequeue(out var work)) work.Callback(work.State);
                Window.Dispatcher.Drain();
            });
        }
    }
}
