using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Services.FeatureHost;

internal static class FeatureAttacherTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Feature attachers validate dependencies and resolve the current app host", () =>
        {
            var fixture = new Fixture();
            fixture.Register<object>("one");
            Throws<ArgumentNullException>(() => new FeatureAttacher(null!));
            Throws<ArgumentNullException>(() => new FeatureAttacher(null!, fixture, fixture.Registry));
            Throws<ArgumentNullException>(() => new FeatureAttacher(new object(), null!, fixture.Registry));
            Throws<ArgumentNullException>(() => new FeatureAttacher(new object(), fixture, null!));
            Throws<InvalidOperationException>(() => new FeatureAttacher(new object()));

            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IFeatureService>(fixture);
                services.AddSingleton(fixture.Registry);
            }).Build();
            using var attacher = new FeatureAttacher(new object())
            {
                FeatureContainer = Container("one")
            };
            attacher.Attach();
            Check(fixture.Created.Count == 1 && fixture.Created[0].Target != null,
                "The host constructor did not use the registered services.");
        });

        run("Feature attachers activate explicitly and have repeatable lifecycle methods", () =>
        {
            var fixture = new Fixture();
            fixture.Register<object>("one");
            fixture.Register<object>("two");
            var container = Container("one");
            using var attacher = fixture.Attacher(container);
            attacher.Reload();
            container.Features.Add(Info("two"));
            Check(fixture.Created.Count == 0, "Configuration activated an unattached helper.");
            attacher.Attach();
            Check(fixture.Created.Count == 2, "Initial activation duplicated or omitted features.");
            attacher.Attach();
            attacher.FeatureContainer = container;
            Check(fixture.Created.Count == 2, "Repeated activation or assignment reloaded features.");
            ExpectReload(fixture, attacher.Reload, "one", "two");
            attacher.Detach();
            var count = fixture.Created.Count;
            var events = fixture.Events.Count;
            attacher.Detach();
            attacher.Reload();
            container.Features.RemoveAt(0);
            Check(fixture.Created.Count == count && fixture.Events.Count == events,
                "Detached helpers kept observing or repeated cleanup.");
            attacher.Attach();
            Check(fixture.Created.Count == count + 1 && fixture.Created[^1].Id == "two",
                "Reactivation did not use the latest configuration.");
        });

        run("Feature attachers filter by runtime target and preserve order and duplicate entries", () =>
        {
            var fixture = new Fixture();
            fixture.Register<object>("object");
            fixture.Register<BaseHost>("base");
            fixture.Register<IProbeHost>("interface");
            fixture.Register<string>("unsupported");
            var duplicate = Info("object");
            var unsupported = Info("unsupported");
            var unknown = Info("unknown");
            var container = new ProbeContainer
            {
                Features = [duplicate, Info("base"), unsupported, unknown, Info("interface"), duplicate]
            };
            var host = new DerivedHost();
            using var attacher = fixture.Attacher(container, host);
            attacher.Attach();
            Check(fixture.Created.Select(probe => probe.Id).SequenceEqual(["object", "base", "interface", "object"])
                && fixture.Created.All(probe => ReferenceEquals(probe.Target, host))
                && fixture.Attempts.Count == 4 && unknown.Settings == null && unsupported.Settings == null,
                "Filtering, ordering, duplicate instances, or runtime target matching was incorrect.");
            attacher.Detach();
            Check(fixture.Events.SequenceEqual(fixture.Created.Select(probe => probe.AttachedEvent)
                .Concat(fixture.Created.AsEnumerable().Reverse().Select(probe => probe.DetachedEvent))),
                "Unload order did not reverse actual attachment order.");
        });

        run("Feature attachers accept descendants without accepting incompatible ancestors", () =>
        {
            (object Host, string[] Expected)[] cases =
            [
                (new BaseHost(), ["base"]),
                (new DerivedHost(), ["base", "derived", "interface"]),
                (new IndirectHost(), ["base", "derived", "indirect", "interface"])
            ];

            foreach (var (host, expected) in cases)
            {
                var fixture = new Fixture();
                fixture.Register<BaseHost>("base");
                fixture.Register<DerivedHost>("derived");
                fixture.Register<IndirectHost>("indirect");
                fixture.Register<IProbeHost>("interface");
                fixture.Register<string>("unrelated");
                using var attacher = fixture.Attacher(
                    Container("base", "derived", "indirect", "interface", "unrelated"), host);
                attacher.Attach();
                Check(fixture.Created.Select(probe => probe.Id).SequenceEqual(expected)
                    && fixture.Attempts.SequenceEqual(expected)
                    && fixture.Created.All(probe => ReferenceEquals(probe.Target, host))
                    && fixture.Logger.Errors.Count == 0,
                    "Target compatibility rejected a descendant or accepted an incompatible ancestor.");
            }
        });

        run("Feature attachers reload exactly once for each collection operation", () =>
        {
            var fixture = new Fixture();
            fixture.Register<object>("one");
            fixture.Register<object>("two");
            fixture.Register<object>("three");
            var container = Container("one");
            using var attacher = fixture.Attacher(container);
            attacher.Attach();
            ExpectReload(fixture, () => container.Features.Add(Info("two")), "one", "two");
            ExpectReload(fixture, () => container.Features.Insert(1, Info("three")), "one", "three", "two");
            ExpectReload(fixture, () => container.Features.Move(2, 0), "two", "one", "three");
            ExpectReload(fixture, () => container.Features[1] = Info("three"), "two", "three", "three");
            ExpectReload(fixture, () => container.Features.RemoveAt(0), "three", "three");
            ExpectReload(fixture, container.Features.Clear);
            ExpectReload(fixture, () => container.Features.Add(Info("one")), "one");
        });

        run("Feature attachers switch containers and collections and release obsolete subscriptions", () =>
        {
            var fixture = new Fixture();
            fixture.Register<object>("one");
            fixture.Register<object>("two");
            var first = Container("one");
            var oldCollection = first.Features;
            var oldItem = oldCollection[0];
            var second = Container("two");
            using var attacher = fixture.Attacher(first);
            attacher.Attach();
            ExpectReload(fixture, () => first.Features = []);
            ExpectReload(fixture, () => first.Features = [Info("two")], "two");
            ExpectReload(fixture, () => attacher.FeatureContainer = second, "two");
            var count = fixture.Created.Count;
            oldCollection.Add(Info("one"));
            oldItem.Id = "two";
            first.Features.Add(Info("one"));
            first.Features[0].Settings = new ProbeSettings();
            first.Features = [];
            Check(fixture.Created.Count == count, "An obsolete source retained an active subscription.");
            ExpectReload(fixture, () => attacher.FeatureContainer = null);
            second.Features.Add(Info("one"));
            Check(fixture.Created.Count == count, "A null container continued observing its predecessor.");
            ExpectReload(fixture, () => attacher.FeatureContainer = second, "two", "one");
        });

        run("Feature attachers observe item IDs and settings replacements but not settings fields", () =>
        {
            var fixture = new Fixture();
            fixture.Register<object>("one", withSettings: true);
            fixture.Register<object>("two", withSettings: true);
            var container = Container("one");
            var item = container.Features[0];
            using var attacher = fixture.Attacher(container);
            attacher.Attach();
            ExpectReload(fixture, () => item.Id = "two", "two");
            var replacement = new ProbeSettings();
            ExpectReload(fixture, () => item.Settings = replacement, "two");
            Check(ReferenceEquals(fixture.Created[^1].Feature.SettingsInternal, replacement),
                "The replacement settings did not reach the new feature.");
            var count = fixture.Created.Count;
            replacement.Value = 1;
            item.Settings = replacement;
            item.Id = "two";
            Check(fixture.Created.Count == count, "Nested or unchanged settings caused a reload.");
            ExpectReload(fixture, container.Features.Clear);
            item.Id = "one";
            item.Settings = new ProbeSettings();
            Check(fixture.Created.Count == count, "Removed items retained active subscriptions.");
        });

        run("Feature attachers suppress settings initialization for repeated references", () =>
        {
            var fixture = new Fixture();
            fixture.Register<object>("one", withSettings: true);
            var item = Info("one");
            var container = new ProbeContainer { Features = [item, item] };
            using var attacher = fixture.Attacher(container);
            attacher.Attach();
            Check(fixture.Created.Count == 2 && item.Settings is ProbeSettings
                && fixture.Created.All(probe => ReferenceEquals(probe.Feature.SettingsInternal, item.Settings)),
                "Settings initialization reloaded duplicate references or failed to share their model.");
            ExpectReload(fixture, () => item.Settings = new OtherSettings(), "one", "one");
            Check(item.Settings is ProbeSettings, "Mismatched settings were not replaced.");
        });

        run("Feature attachers integrate with real feature service settings injection", () =>
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IFeatureService, FeatureService>();
            services.AddFeature<InjectedFeature>();
            using var provider = services.BuildServiceProvider();
            var item = Info("attacher.injected");
            using var attacher = new FeatureAttacher(
                new object(), provider.GetRequiredService<IFeatureService>(),
                provider.GetRequiredService<FeatureRegistryService>())
            {
                FeatureContainer = new ProbeContainer { Features = [item] }
            };
            InjectedFeature.AttachCalls = 0;
            InjectedFeature.DetachCalls = 0;
            attacher.Attach();
            Check(InjectedFeature.AttachCalls == 1 && item.Settings is ProbeSettings,
                "Real settings injection caused recursive activation.");
            item.Settings = new ProbeSettings();
            Check(InjectedFeature.AttachCalls == 2, "Real service settings replacement was not observed.");
            attacher.Dispose();
            Check(InjectedFeature.DetachCalls == 2, "Reload or disposal did not clean up the owned features.");
            provider.Dispose();
            Check(InjectedFeature.DetachCalls == 2, "Host disposal repeated cleanup of already disposed features.");
        });

        run("Feature attachers isolate creation attachment and cleanup failures", () =>
        {
            var fixture = new Fixture();
            foreach (var id in new[] { "one", "create-error", "attach-error", "detach-error", "two" })
            {
                fixture.Register<object>(id);
            }

            fixture.Creating = id =>
            {
                if (id == "create-error")
                {
                    throw new InvalidOperationException("creation failed");
                }
            };
            fixture.Attaching = probe =>
            {
                if (probe.Id == "attach-error")
                {
                    throw new InvalidOperationException("attachment failed");
                }
            };
            fixture.Detaching = probe =>
            {
                if (probe.Id == "detach-error")
                {
                    throw new InvalidOperationException("cleanup failed");
                }
            };
            using var attacher = fixture.Attacher(Container("one", "create-error", "attach-error", "detach-error", "two"));
            attacher.Attach();
            Check(fixture.Attempts.Count == 5 && fixture.Created.Single(probe => probe.Id == "attach-error").DetachCalls == 1
                && fixture.Created[^1].Target != null && fixture.Logger.Errors.Count == 2,
                "A failed feature blocked later features or skipped partial attachment cleanup.");
            attacher.Detach();
            Check(fixture.Created.All(probe => probe.DetachCalls == 1) && fixture.Logger.Errors.Count == 3,
                "A cleanup failure blocked another feature or cleanup ran more than once.");
            Check(fixture.Logger.Errors.Any(message => message.Contains("create-error"))
                && fixture.Logger.Errors.Any(message => message.Contains("attach-error"))
                && fixture.Logger.Errors.Any(message => message.Contains("detach-error")),
                "Failure logs omitted the feature ID.");
        });

        run("Feature attachers serialize collection changes raised during attachment", () =>
        {
            var fixture = new Fixture();
            fixture.Register<object>("one");
            fixture.Register<object>("two");
            var container = Container("one");
            var changed = false;
            var depth = 0;
            var maxDepth = 0;
            fixture.Attaching = _ =>
            {
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                if (!changed)
                {
                    changed = true;
                    container.Features.Add(Info("two"));
                }

                depth--;
            };
            using var attacher = fixture.Attacher(container);
            attacher.Attach();
            Check(maxDepth == 1 && fixture.Created.Count == 3 && fixture.Created[0].DetachCalls == 1
                && fixture.Created.Skip(1).Select(probe => probe.Id).SequenceEqual(["one", "two"])
                && fixture.Created.Skip(1).All(probe => probe.Target != null),
                "Reentrant collection changes were lost or caused recursive attachment.");
        });

        run("Feature attachers retain ID changes raised during feature construction", () =>
        {
            var fixture = new Fixture();
            fixture.Register<object>("one", withSettings: true);
            fixture.Register<object>("two", withSettings: true);
            var container = Container("one");
            var changed = false;
            fixture.Creating = _ =>
            {
                if (!changed)
                {
                    changed = true;
                    container.Features[0].Id = "two";
                }
            };
            using var attacher = fixture.Attacher(container);
            attacher.Attach();
            Check(fixture.Created.Count == 2 && fixture.Created[0].AttachCalls == 0
                && fixture.Created[0].DetachCalls == 1 && fixture.Created[1].Id == "two"
                && fixture.Created[1].Target != null,
                "Initialization suppression swallowed an ID update or attached an obsolete feature.");
        });

        run("Feature attachers retain container replacements raised during attachment", () =>
        {
            var fixture = new Fixture();
            fixture.Register<object>("one");
            fixture.Register<object>("two");
            var first = Container("one");
            var second = Container("two");
            using var attacher = fixture.Attacher(first);
            fixture.Attaching = probe =>
            {
                if (probe.Id == "one")
                {
                    attacher.FeatureContainer = second;
                }
            };
            attacher.Attach();
            Check(fixture.Created.Count == 2 && fixture.Created[0].DetachCalls == 1
                && fixture.Created[1].Id == "two" && fixture.Created[1].Target != null,
                "A reentrant container replacement was lost.");
            var count = fixture.Created.Count;
            first.Features.Add(Info("one"));
            Check(fixture.Created.Count == count, "A reentrant switch retained the old container.");
            ExpectReload(fixture, () => second.Features.Add(Info("two")), "two", "two");
        });

        run("Feature attachers finish cleanup when detached or disposed during attachment", () =>
        {
            foreach (var dispose in new[] { false, true })
            {
                var fixture = new Fixture();
                fixture.Register<object>("one");
                fixture.Register<object>("two");
                var container = Container("one", "two");
                using var attacher = fixture.Attacher(container);
                fixture.Attaching = probe =>
                {
                    if (probe.Id == "two")
                    {
                        if (dispose)
                        {
                            attacher.Dispose();
                        }
                        else
                        {
                            attacher.Detach();
                        }
                    }
                };
                attacher.Attach();
                Check(fixture.Created.Count == 2 && fixture.Created.All(probe => probe.DetachCalls == 1),
                    "A lifecycle callback left a partially attached feature alive.");
                container.Features.Add(Info("one"));
                Check(fixture.Created.Count == 2, "A lifecycle callback left an active subscription.");
                attacher.Dispose();
            }
        });

        run("Disposed feature attachers reject operations and release all subscriptions", () =>
        {
            var fixture = new Fixture();
            fixture.Register<object>("one");
            var container = Container("one");
            using var attacher = fixture.Attacher(container);
            attacher.Attach();
            attacher.Dispose();
            attacher.Dispose();
            container.Features[0].Id = "unknown";
            container.Features[0].Settings = new ProbeSettings();
            container.Features.Add(Info("one"));
            container.Features = [Info("one")];
            Check(fixture.Created.Count == 1 && fixture.Created[0].DetachCalls == 1,
                "Disposed helpers continued observing or disposed features repeatedly.");
            Throws<ObjectDisposedException>(attacher.Attach);
            Throws<ObjectDisposedException>(attacher.Detach);
            Throws<ObjectDisposedException>(attacher.Reload);
            Throws<ObjectDisposedException>(() => attacher.FeatureContainer = container);
            Throws<ObjectDisposedException>(() => _ = attacher.FeatureContainer);
        });
    }

    private static FeatureInfo Info(string id) => new() { Id = id };

    private static ProbeContainer Container(params string[] ids) => new()
    {
        Features = new ObservableCollection<FeatureInfo>(ids.Select(Info))
    };

    private static void ExpectReload(Fixture fixture, Action action, params string[] expectedIds)
    {
        var previous = fixture.Created.Where(probe => probe.Target != null).ToArray();
        var count = fixture.Created.Count;
        fixture.Events.Clear();
        action();
        var created = fixture.Created.Skip(count).ToArray();
        Check(created.Select(probe => probe.Id).SequenceEqual(expectedIds),
            "A change did not create exactly one fresh instance per configured feature.");
        Check(previous.All(probe => probe.DetachCalls == 1) && created.All(probe => probe.Target != null),
            "Reload retained old instances or did not attach new instances.");
        Check(fixture.Events.SequenceEqual(previous.Reverse().Select(probe => probe.DetachedEvent)
            .Concat(created.Select(probe => probe.AttachedEvent))),
            "Reload did not finish reverse cleanup before ordered attachment.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new Exception($"Expected {typeof(TException).Name}.");
    }

    private sealed class Fixture : IFeatureService
    {
        private readonly List<FeatureRegisterInfo> _registrations = [];
        private readonly Dictionary<string, Func<FeatureBase>> _factories = [];

        public List<Probe> Created { get; } = [];

        public List<string> Attempts { get; } = [];

        public List<string> Events { get; } = [];

        public ProbeLogger Logger { get; } = new();

        public Action<string>? Creating { get; set; }

        public Action<Probe>? Attaching { get; set; }

        public Action<Probe>? Detaching { get; set; }

        public FeatureRegistryService Registry => new(_registrations);

        public void Register<TTarget>(string id, bool withSettings = false) where TTarget : class
        {
            _registrations.Add(new FeatureRegisterInfo(
                id, id, "", typeof(ProbeFeature<TTarget>), typeof(TTarget),
                withSettings ? typeof(ProbeSettings) : null, null));
            _factories.Add(id, () =>
            {
                var probe = new Probe(this, id, Created.Count + 1);
                var feature = new ProbeFeature<TTarget>(probe);
                probe.Feature = feature;
                Created.Add(probe);
                return feature;
            });
        }

        public FeatureAttacher Attacher(IFeatureContainer container, object? host = null) => new(
            host ?? new object(), this, Registry, Logger)
        {
            FeatureContainer = container
        };

        public FeatureBase? CreateFeature(FeatureInfo settings)
        {
            var id = settings.Id;
            Attempts.Add(id);
            Creating?.Invoke(id);
            var registration = _registrations.Single(info => info.Id == id);
            var model = registration.ClientSettingsType == null ? null : settings.GetSettingsOrCreate<ProbeSettings>();
            var feature = _factories[id]();
            feature.SetSettings(model);
            settings.Settings = model;
            return feature;
        }

        public ControlWithSettingsBase? CreateSettingsControl(FeatureInfo settings) => null;
    }

    private sealed class Probe(Fixture fixture, string id, int number)
    {
        public string Id { get; } = id;

        public string AttachedEvent => $"attach:{Id}:{number}";

        public string DetachedEvent => $"detach:{Id}:{number}";

        public FeatureBase Feature { get; set; } = null!;

        public object? Target { get; private set; }

        public int AttachCalls { get; private set; }

        public int DetachCalls { get; private set; }

        public void Attach(object target)
        {
            Target = target;
            AttachCalls++;
            fixture.Events.Add(AttachedEvent);
            fixture.Attaching?.Invoke(this);
        }

        public void Detach()
        {
            Target = null;
            DetachCalls++;
            fixture.Events.Add(DetachedEvent);
            fixture.Detaching?.Invoke(this);
        }
    }

    private sealed class ProbeFeature<TTarget>(Probe probe) : FeatureBase<TTarget> where TTarget : class
    {
        public override void Attach(TTarget target)
        {
            Target = target;
            probe.Attach(target);
        }

        public override void Detach()
        {
            Target = null;
            probe.Detach();
        }
    }

    private sealed class ProbeContainer : ObservableObject, IFeatureContainer
    {
        private ObservableCollection<FeatureInfo> _features = [];

        public ObservableCollection<FeatureInfo> Features
        {
            get => _features;
            set => SetProperty(ref _features, value);
        }
    }

    private sealed class ProbeSettings : ObservableObject, IFeatureSettings
    {
        private int _value;

        public int Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }
    }

    private sealed class OtherSettings : IFeatureSettings;

    private interface IProbeHost;

    private class BaseHost;

    private class DerivedHost : BaseHost, IProbeHost;

    private sealed class IndirectHost : DerivedHost;

    [FeatureInfo("attacher.injected", "Injected")]
    private sealed class InjectedFeature : FeatureBase<object, ProbeSettings>
    {
        public static int AttachCalls { get; set; }

        public static int DetachCalls { get; set; }

        public override void Attach(object target)
        {
            Target = target;
            AttachCalls++;
            _ = Settings;
        }

        public override void Detach()
        {
            Target = null;
            DetachCalls++;
        }
    }

    private sealed class ProbeLogger : ILogger<FeatureAttacher>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }
}
