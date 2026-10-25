using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.UI.Xaml;

internal static class PresenterFeatureTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Scene layer and component presenters attach matching features only while loaded", () =>
        {
            var cases = Cases();
            foreach (var item in cases)
            {
                var container = item.CreateContainer();
                container.Features = [Info("first"), Info(item.FeatureId), Info("second")];
                item.SetContainer(item.Presenter, container);
            }

            var log = CreateHost();
            Check(log.Entries.Count == 0, "Constructing or configuring a presenter resolved features eagerly.");
            foreach (var item in cases)
            {
                var count = log.Entries.Count;
                item.Presenter.Load();
                item.Presenter.Load();
                var entries = log.Entries.Skip(count).ToArray();
                Check(entries.Select(entry => entry.Id).SequenceEqual(["first", item.FeatureId, "second"]) && entries.All(entry => ReferenceEquals(entry.Target, item.Presenter)), "A presenter did not attach its own features in order or repeated Loaded duplicated them.");
                log.Detached.Clear();
                item.Presenter.Unload();
                item.Presenter.Unload();
                Check(log.Detached.SequenceEqual(entries.Reverse()) && entries.All(entry => entry.DetachCalls == 1), "Presenter unload did not clean up once in reverse order.");
            }
        });
        run("Presenter feature sources follow configuration replacements and collection changes", () =>
        {
            var log = CreateHost();
            foreach (var item in Cases())
            {
                var original = item.CreateContainer();
                original.Features = [Info("first")];
                item.SetContainer(item.Presenter, original);
                item.Presenter.Load();
                ExpectCreated(log, () => original.Features.Add(Info("second")), "first", "second");
                ExpectCreated(log, () => original.Features.Move(1, 0), "second", "first");
                var oldCollection = original.Features;
                ExpectCreated(log, () => original.Features = [Info(item.FeatureId)], item.FeatureId);
                var replacement = item.CreateContainer();
                replacement.Features = [Info("second")];
                ExpectCreated(log, () => item.SetContainer(item.Presenter, replacement), "second");
                var count = log.Entries.Count;
                original.Features.Add(Info("first"));
                oldCollection[0].Id = "second";
                Check(log.Entries.Count == count, "A presenter retained its old configuration subscriptions.");
                var active = log.Entries[^1];
                item.SetContainer(item.Presenter, null);
                Check(active.DetachCalls == 1, "Clearing presenter configuration left a feature attached.");
                ExpectCreated(log, () => item.SetContainer(item.Presenter, replacement), "second");
                item.Presenter.Unload();
            }
        });
        run("Unloaded presenters ignore feature edits and reload their latest configuration", () =>
        {
            var log = CreateHost();
            foreach (var item in Cases())
            {
                var container = item.CreateContainer();
                container.Features = [Info("first")];
                item.SetContainer(item.Presenter, container);
                item.Presenter.Load();
                item.Presenter.Unload();
                var count = log.Entries.Count;
                container.Features[0].Id = "second";
                container.Features.Add(Info(item.FeatureId));
                var replacement = item.CreateContainer();
                replacement.Features = [Info("first"), Info("second")];
                item.SetContainer(item.Presenter, replacement);
                Check(log.Entries.Count == count, "An unloaded presenter recreated features.");
                ExpectCreated(log, item.Presenter.Load, "first", "second");
                item.Presenter.Unload();
            }
        });
        run("Presenters sharing settings keep independent feature instances and lifetimes", () =>
        {
            var log = CreateHost();
            var firstCases = Cases();
            var secondCases = Cases();
            for (var index = 0; index < firstCases.Length; index++)
            {
                var first = firstCases[index];
                var second = secondCases[index];
                var container = first.CreateContainer();
                container.Features = [Info(first.FeatureId)];
                first.SetContainer(first.Presenter, container);
                second.SetContainer(second.Presenter, container);
                first.Presenter.Load();
                var firstEntry = log.Entries[^1];
                second.Presenter.Load();
                var secondEntry = log.Entries[^1];
                Check(!ReferenceEquals(firstEntry, secondEntry) && ReferenceEquals(firstEntry.Target, first.Presenter) && ReferenceEquals(secondEntry.Target, second.Presenter), "Shared settings shared a feature instance across presenters.");
                first.Presenter.Unload();
                Check(firstEntry.DetachCalls == 1 && secondEntry.DetachCalls == 0, "Unloading one presenter detached another presenter's feature.");
                ExpectCreated(log, () => container.Features.Add(Info("second")), first.FeatureId, "second");
                second.Presenter.Unload();
            }
        });
        run("Component presenter detaches features before replacing or clearing content", () =>
        {
            var log = CreateHost();
            var presenter = new ComponentPresenter
            {
                Settings = new ComponentSettings
                {
                    Id = TextComponent.Id,
                    Features = [Info("first")]
                }
            };
            presenter.Load();
            var first = log.Entries[^1];
            Check(first.AttachedContent is TextComponent && ReferenceEquals(first.AttachedContent, presenter.Content), "Component features attached before component content was available.");
            presenter.Settings = new ComponentSettings
            {
                Id = TextComponent.Id,
                Features = [Info("second")]
            };
            var second = log.Entries[^1];
            Check(first.DetachCalls == 1 && ReferenceEquals(first.DetachedContent, first.AttachedContent) && second.AttachedContent is TextComponent && !ReferenceEquals(first.AttachedContent, second.AttachedContent), "Component replacement detached old features after replacing content or attached new features too early.");
            presenter.Settings.Settings = new TextComponentSettings();
            var third = log.Entries[^1];
            Check(second.DetachCalls == 1 && ReferenceEquals(second.DetachedContent, second.AttachedContent) && third.AttachedContent is TextComponent && !ReferenceEquals(second.AttachedContent, third.AttachedContent), "Custom settings replacement did not reattach features around the new component content.");
            presenter.Settings.Id = "unknown";
            Check(third.DetachCalls == 1 && log.Entries[^1].AttachedContent == null && presenter.Content == null, "Component ID replacement did not detach features before clearing obsolete content.");
            presenter.Settings.Id = TextComponent.Id;
            var last = log.Entries[^1];
            presenter.Unload();
            Check(last.DetachCalls == 1 && last.AttachedContent is TextComponent && ReferenceEquals(last.DetachedContent, last.AttachedContent) && presenter.Content == null, "Component unload cleared content before cleaning up features.");
        });
    }

    private static FeatureInfo Info(string id) => new()
    {
        Id = "presenter." + id
    };
    private static PresenterCase[] Cases() => [new(new ScenePresenter(), () => new SceneSettings(), (presenter, container) => ((ScenePresenter)presenter).Scene = (SceneSettings?)container, "scene"), new(new LayerPresenter(), () => new LayerSettings(), (presenter, container) => ((LayerPresenter)presenter).Layer = (LayerSettings?)container, "layer"), new(new ComponentPresenter(), () => new ComponentSettings { Id = TextComponent.Id }, (presenter, container) => ((ComponentPresenter)presenter).Settings = (ComponentSettings?)container, "component")];
    private static ProbeLog CreateHost()
    {
        var log = new ProbeLog();
        IAppHost.Host = new HostBuilder().ConfigureLogging(logging => logging.AddDebug()).ConfigureServices(services =>
        {
            services.AddSingleton(log);
            services.AddSingleton<IFeatureService, FeatureService>();
            services.AddSingleton<IComponentsService, ComponentsService>();
            services.AddComponent<TextComponent>();
            services.AddFeature<FirstFeature>();
            services.AddFeature<SecondFeature>();
            services.AddFeature<SceneFeature>();
            services.AddFeature<LayerFeature>();
            services.AddFeature<ComponentFeature>();
        }).Build();
        return log;
    }

    private static void ExpectCreated(ProbeLog log, Action action, params string[] ids)
    {
        var count = log.Entries.Count;
        action();
        Check(log.Entries.Skip(count).Select(entry => entry.Id).SequenceEqual(ids), "A presenter change did not attach exactly the expected fresh feature instances.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private sealed record PresenterCase(FrameworkElement Presenter, Func<IFeatureContainer> CreateContainer, Action<FrameworkElement, IFeatureContainer?> SetContainer, string FeatureId);
    private sealed class ProbeLog
    {
        public List<Entry> Entries { get; } = [];
        public List<Entry> Detached { get; } = [];
    }

    private sealed class Entry(string id, FrameworkElement target)
    {
        public string Id { get; } = id;
        public FrameworkElement Target { get; } = target;
        public object? AttachedContent { get; } = (target as ComponentPresenter)?.Content;
        public object? DetachedContent
        {
            get; set;
        }
        public int DetachCalls
        {
            get; set;
        }
    }

    private abstract class TrackingFeature<TTarget>(ProbeLog log, string id) : FeatureBase<TTarget> where TTarget : FrameworkElement
    {
        private Entry? _entry;
        public override void Attach(TTarget target)
        {
            Target = target;
            _entry = new Entry(id, target);
            log.Entries.Add(_entry);
        }

        public override void Detach()
        {
            if (_entry != null)
            {
                _entry.DetachedContent = (Target as ComponentPresenter)?.Content;
                _entry.DetachCalls++;
                log.Detached.Add(_entry);
            }

            Target = null;
        }
    }

    [FeatureInfo("presenter.first", "First")]
    private sealed class FirstFeature(ProbeLog log) : TrackingFeature<FrameworkElement>(log, "first");
    [FeatureInfo("presenter.second", "Second")]
    private sealed class SecondFeature(ProbeLog log) : TrackingFeature<FrameworkElement>(log, "second");
    [FeatureInfo("presenter.scene", "Scene")]
    private sealed class SceneFeature(ProbeLog log) : TrackingFeature<ScenePresenter>(log, "scene");
    [FeatureInfo("presenter.layer", "Layer")]
    private sealed class LayerFeature(ProbeLog log) : TrackingFeature<LayerPresenter>(log, "layer");
    [FeatureInfo("presenter.component", "Component")]
    private sealed class ComponentFeature(ProbeLog log) : TrackingFeature<ComponentPresenter>(log, "component");
}