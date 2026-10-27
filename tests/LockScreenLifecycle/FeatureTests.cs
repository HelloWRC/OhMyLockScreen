using Microsoft.Extensions.DependencyInjection;
using OhMyLockScreen;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Attributes.FeatureHost;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml.Controls;

internal static class FeatureTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Feature registration rejects invalid types and metadata without partial registration", () =>
        {
            var services = new ServiceCollection();
            services.AddFeature<ProbeFeature>();
            var count = services.Count;
            Throws<ArgumentException>(() => services.AddFeature<ProbeFeature>());
            Throws<ArgumentException>(() => services.AddFeature<DuplicateFeature>());
            Throws<ArgumentException>(() => services.AddFeature<MissingInfoFeature>());
            Throws<ArgumentException>(() => services.AddFeature<AbstractFeature>());
            Throws<ArgumentException>(() => services.AddFeature<EmptyIdFeature>());
            Throws<ArgumentException>(() => services.AddFeature<EmptyNameFeature>());
            Throws<ArgumentException>(() => services.AddFeature<InheritedInfoFeature>());
            Throws<ArgumentException>(() => services.AddFeature<NotAFeature>());
            Throws<ArgumentException>(() => services.AddFeature<PlainFeature, AbstractSettingsControl>());
            Throws<ArgumentException>(() => services.AddFeature<PlainFeature, ProbeSettingsControl>());
            Throws<ArgumentException>(() => services.AddFeature<WithoutControlFeature, WrongSettingsControl>());
            Throws<ArgumentNullException>(() => FeatureRegistryExtensions.AddFeature<PlainFeature>(null!));
            Check(services.Count == count, "Failed registration modified the service collection.");
        });
        run("Controls with settings support arbitrary models and notify only when the model changes", () =>
        {
            var control = new GeneralSettingsControl();
            var notifications = 0;
            control.PropertyChanged += (_, args) =>
            {
                Check(args.PropertyName == nameof(control.Settings), "Settings injection notified an unrelated property.");
                notifications++;
            };
            Throws<InvalidOperationException>(() => _ = control.Settings);
            var first = new GeneralSettings();
            control.SetSettings(first);
            Check(ReferenceEquals(control.Settings, first) && notifications == 1 && control.SettingsCallbacks == 1, "The control did not expose or notify its injected settings.");
            control.SetSettings(first);
            Check(notifications == 1 && control.SettingsCallbacks == 1, "Repeated injection duplicated notifications.");
            var second = new GeneralSettings();
            control.SetSettings(second);
            Check(ReferenceEquals(control.Settings, second) && notifications == 2 && control.SettingsCallbacks == 2, "Settings replacement did not update the control.");
            control.SetSettings(null);
            Throws<InvalidOperationException>(() => _ = control.Settings);
            Check(notifications == 3 && control.SettingsCallbacks == 3, "Clearing settings did not notify the control.");
        });
        run("Feature settings controls preserve typed settings notifications when replaced or cleared", () =>
        {
            var control = new ProbeSettingsControl(new ProbeDependency());
            Throws<InvalidOperationException>(() => _ = control.Settings);
            var first = new ProbeSettings();
            control.SetSettings(first);
            Check(ReferenceEquals(control.Settings, first) && control.SettingsNotifications == 1 && control.SettingsCallbacks == 1, "Feature settings injection did not expose or notify the model.");
            control.SetSettings(first);
            Check(control.SettingsNotifications == 1 && control.SettingsCallbacks == 1, "Repeated injection duplicated feature settings notifications.");
            var second = new ProbeSettings();
            control.SetSettings(second);
            Check(ReferenceEquals(control.Settings, second) && control.SettingsNotifications == 2 && control.SettingsCallbacks == 2, "Replacing feature settings did not update the control.");
            control.SetSettings(null);
            Throws<InvalidOperationException>(() => _ = control.Settings);
            Check(control.SettingsInternal == null && control.SettingsNotifications == 3 && control.SettingsCallbacks == 3, "Clearing feature settings did not notify the control.");
        });
        run("Built-in feature settings controls share the target property without sharing target values", () =>
        {
            FeatureSettingsControlBase[] controls =
            [
                new OhMyLockScreen.Controls.Editor.BackgroundSettingsControl(),
                new OhMyLockScreen.Controls.Editor.BorderSettingsControl(),
                new OhMyLockScreen.Controls.Editor.ForegroundSettingsControl(),
                new OhMyLockScreen.Controls.Editor.LayoutSettingsControl(),
                new OhMyLockScreen.Controls.Editor.GridLayoutSettingsControl()
            ];
            Check(controls.All(control => control.TargetElement == null), "Feature editor targets did not default to null.");
            var first = new Border();
            var second = new UserControl();
            controls[0].TargetElement = first;
            Check(controls.Skip(1).All(control => control.TargetElement == null), "Assigning one feature editor target changed another control.");
            controls[1].SetValue(FeatureSettingsControlBase.TargetElementProperty, second);
            Check(ReferenceEquals(controls[0].TargetElement, first) && ReferenceEquals(controls[1].TargetElement, second), "The inherited target property did not preserve independent values.");
            controls[0].TargetElement = second;
            controls[0].TargetElement = null;
            Check(controls[0].GetValue(FeatureSettingsControlBase.TargetElementProperty) == null && ReferenceEquals(controls[1].TargetElement, second), "Clearing an editor target changed another control.");
        });
        run("Feature registration infers targets and settings across intermediate generic bases", () =>
        {
            var services = new ServiceCollection();
            services.AddFeature<ProbeFeature>();
            services.AddFeature<PlainFeature>();
            using var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<FeatureRegistryService>();
            var probe = registry.Find("test.probe")!;
            Check(registry.Registered.Count == 2 && probe.FeatureType == typeof(ProbeFeature) && probe.TargetType == typeof(UserControl) && probe.ClientSettingsType == typeof(ProbeSettings) && probe.SettingsType == null && probe.Name == "Probe" && probe.Description == "Probe description", "Feature registration lost metadata or inferred incorrect generic arguments.");
            var plain = registry.Find("test.plain")!;
            Check(plain.TargetType == typeof(object) && plain.ClientSettingsType == null && plain.SettingsType == null && plain.Description == "Plain description" && plain.IconSource == null, "A feature without custom settings or an icon was registered incorrectly.");
            Check(registry.Find("unknown") == null && registry.Find("TEST.PROBE") == null, "Feature lookup did not use exact IDs.");
            var firstIcon = probe.IconSource!;
            var secondIcon = probe.IconSource!;
            Check(firstIcon.Glyph == "\U000F07F0" && secondIcon.Glyph == firstIcon.Glyph && !ReferenceEquals(firstIcon, secondIcon), "Feature icon sources were lost or shared.");
            firstIcon.Glyph = "changed";
            Check(probe.IconSource!.Glyph == "\U000F07F0", "An icon source changed stored feature metadata.");
        });
        run("Feature and settings controls use transient constructor injection without eager creation", () =>
        {
            var services = new ServiceCollection();
            var dependency = new ProbeDependency();
            services.AddSingleton(dependency);
            services.AddFeature<ProbeFeature, ProbeSettingsControl>();
            using var provider = services.BuildServiceProvider();
            var info = provider.GetRequiredService<FeatureRegistryService>().Find("test.probe")!;
            Check(info.SettingsType == typeof(ProbeSettingsControl) && info.ClientSettingsType == typeof(ProbeSettings) && dependency.FeaturesCreated == 0 && dependency.ControlsCreated == 0, "Registration instantiated a feature or confused the settings control and model.");
            var first = provider.GetRequiredService<ProbeFeature>();
            var second = provider.GetRequiredService<ProbeFeature>();
            var firstControl = provider.GetRequiredService<ProbeSettingsControl>();
            var secondControl = provider.GetRequiredService<ProbeSettingsControl>();
            Throws<InvalidOperationException>(() => _ = firstControl.Settings);
            Throws<InvalidOperationException>(() => _ = secondControl.Settings);
            Check(!ReferenceEquals(first, second) && ReferenceEquals(first.Dependency, dependency) && ReferenceEquals(second.Dependency, dependency) && dependency.FeaturesCreated == 2, "Features did not use transient constructor injection.");
            Check(!ReferenceEquals(firstControl, secondControl) && ReferenceEquals(firstControl.Dependency, dependency) && ReferenceEquals(secondControl.Dependency, dependency) && dependency.ControlsCreated == 2, "Settings controls did not use transient constructor injection.");
        });
        run("Feature registries are singletons scoped to their hosts", () =>
        {
            var first = new ServiceCollection();
            first.AddFeature<ProbeFeature>();
            var second = new ServiceCollection();
            second.AddFeature<PlainFeature>();
            using var firstProvider = first.BuildServiceProvider();
            using var secondProvider = second.BuildServiceProvider();
            var firstRegistry = firstProvider.GetRequiredService<FeatureRegistryService>();
            var secondRegistry = secondProvider.GetRequiredService<FeatureRegistryService>();
            Check(ReferenceEquals(firstRegistry, firstProvider.GetRequiredService<FeatureRegistryService>()) && firstRegistry.Find("test.plain") == null && secondRegistry.Find("test.probe") == null, "Feature registry lifetime or host isolation was incorrect.");
        });
        run("Feature service creates independent features with shared settings and injected dependencies", () =>
        {
            using var provider = CreateProvider();
            var service = provider.GetRequiredService<IFeatureService>();
            var settings = new FeatureInfo
            {
                Id = "test.probe"
            };
            var first = (ProbeFeature)service.CreateFeature(settings)!;
            var second = (ProbeFeature)service.CreateFeature(settings)!;
            var dependency = provider.GetRequiredService<ProbeDependency>();
            Check(!ReferenceEquals(first, second) && ReferenceEquals(first.Dependency, dependency) && dependency.FeaturesCreated == 2 && dependency.ControlsCreated == 0, "Feature creation shared instances or created settings controls eagerly.");
            Check(settings.Settings is ProbeSettings && ReferenceEquals(first.Settings, settings.Settings) && ReferenceEquals(second.Settings, settings.Settings) && first.SettingsNotifications == 1 && second.SettingsNotifications == 1 && first.SettingsCallbacks == 1 && second.SettingsCallbacks == 1, "Typed settings were not shared or initialization did not notify exactly once.");
            Check(first.Target == null && second.Target == null, "Creation attached a target.");
            var target = new UserControl();
            first.Attach(target);
            Check(ReferenceEquals(first.Target, target), "The created feature could not attach to its typed target.");
            first.Dispose();
            Check(first.Target == null, "Feature disposal did not detach its target.");
            Check(service.CreateFeature(new FeatureInfo { Id = "test.plain" }) is PlainFeature, "A feature without custom settings could not be created.");
        });
        run("Feature settings controls initialize and reuse the model without creating features", () =>
        {
            using var provider = CreateProvider();
            var service = provider.GetRequiredService<IFeatureService>();
            var settings = new FeatureInfo
            {
                Id = "test.probe"
            };
            var first = (ProbeSettingsControl)service.CreateSettingsControl(settings)!;
            var second = (ProbeSettingsControl)service.CreateSettingsControl(settings)!;
            var dependency = provider.GetRequiredService<ProbeDependency>();
            Check(settings.Settings is ProbeSettings && ReferenceEquals(first.DataContext, settings.Settings) && ReferenceEquals(second.DataContext, settings.Settings) && ReferenceEquals(first.Settings, settings.Settings) && ReferenceEquals(second.Settings, settings.Settings) && first.SettingsCallbacks == 1 && first.SettingsNotifications == 1 && second.SettingsCallbacks == 1 && second.SettingsNotifications == 1 && !ReferenceEquals(first, second) && ReferenceEquals(first.Dependency, dependency) && dependency.ControlsCreated == 2 && dependency.FeaturesCreated == 0, "Settings controls did not use independent instances and the shared model.");
            var feature = (ProbeFeature)service.CreateFeature(settings)!;
            Check(ReferenceEquals(feature.Settings, first.DataContext), "The feature replaced the settings control's model.");
        });
        run("Unknown features and absent settings controls preserve configuration", () =>
        {
            using var provider = CreateProvider();
            var service = provider.GetRequiredService<IFeatureService>();
            var original = new OtherSettings();
            var unknown = new FeatureInfo
            {
                Id = "unknown",
                Settings = original
            };
            Check(service.CreateFeature(unknown) == null && service.CreateSettingsControl(unknown) == null && ReferenceEquals(unknown.Settings, original), "Unknown IDs changed the configuration.");
            var withoutControl = new FeatureInfo
            {
                Id = "test.without-control"
            };
            Check(service.CreateSettingsControl(withoutControl) == null && withoutControl.Settings == null, "An absent settings control created a model.");
            var plain = new FeatureInfo
            {
                Id = "test.plain"
            };
            Check(service.CreateSettingsControl(plain) is PlainSettingsControl { DataContext: null, SettingsInternal: null } && plain.Settings == null, "A settings control without a model could not be created.");
            Throws<ArgumentNullException>(() => service.CreateFeature(null!));
            Throws<ArgumentNullException>(() => service.CreateSettingsControl(null!));
        });
        run("Feature service replaces mismatched settings using the shared settings helper", () =>
        {
            using var provider = CreateProvider();
            var service = provider.GetRequiredService<IFeatureService>();
            var original = new OtherSettings();
            var featureSettings = new FeatureInfo
            {
                Id = "test.probe",
                Settings = original
            };
            var feature = (ProbeFeature)service.CreateFeature(featureSettings)!;
            Check(featureSettings.Settings is ProbeSettings && ReferenceEquals(feature.Settings, featureSettings.Settings), "Feature creation did not replace mismatched settings with its registered model.");
            var controlSettings = new FeatureInfo
            {
                Id = "test.probe",
                Settings = original
            };
            var control = (ProbeSettingsControl)service.CreateSettingsControl(controlSettings)!;
            Check(controlSettings.Settings is ProbeSettings && ReferenceEquals(control.DataContext, controlSettings.Settings) && ReferenceEquals(control.Settings, controlSettings.Settings), "Settings control creation did not replace mismatched settings with its registered model.");
        });
        run("Feature and settings control failures retain the model created by the shared settings helper", () =>
        {
            using var provider = CreateProvider();
            var service = provider.GetRequiredService<IFeatureService>();
            var dependency = provider.GetRequiredService<ProbeDependency>();
            var settings = new FeatureInfo
            {
                Id = "test.probe"
            };
            dependency.FailFeatureConstruction = true;
            Throws<InvalidOperationException>(() => service.CreateFeature(settings));
            var createdSettings = settings.Settings;
            Check(createdSettings is ProbeSettings, "The shared helper did not initialize settings before feature creation.");
            dependency.FailFeatureConstruction = false;
            dependency.FailSettingsInitialization = true;
            Throws<InvalidOperationException>(() => service.CreateFeature(settings));
            Check(ReferenceEquals(settings.Settings, createdSettings), "Feature initialization failure replaced the existing model.");
            dependency.FailSettingsInitialization = false;
            dependency.FailControlConstruction = true;
            Throws<InvalidOperationException>(() => service.CreateSettingsControl(settings));
            Check(ReferenceEquals(settings.Settings, createdSettings), "Settings control failure replaced the existing model.");
            dependency.FailControlConstruction = false;
            dependency.FailControlInitialization = true;
            Throws<InvalidOperationException>(() => service.CreateSettingsControl(settings));
            Check(ReferenceEquals(settings.Settings, createdSettings), "Settings injection failure replaced the existing model.");
        });
        run("The app host provides the built-in features and feature service", () =>
        {
            new App().Launch(new LaunchActivatedEventArgs());
            var registered = IAppHost.Host!.Services.GetRequiredService<FeatureRegistryService>().Registered;
            Check(registered.Select(feature => feature.Id).ToHashSet().SetEquals(
                ["omls.features.layout.layout", "omls.features.layout.grid", "omls.features.appearance.background",
                    "omls.features.appearance.border", "omls.features.appearance.foreground",
                    "omls.features.appearance.text", "omls.features.appearance.font"]),
                "The app host did not register the built-in features.");
            Check(IAppHost.Host.Services.GetRequiredService<IFeatureService>() is FeatureService, "The app host did not register the feature service.");
        });
        FeatureAttacherTests.Run(run);
        PresenterFeatureTests.Run(run);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ProbeDependency>();
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddFeature<ProbeFeature, ProbeSettingsControl>();
        services.AddFeature<PlainFeature, PlainSettingsControl>();
        services.AddFeature<WithoutControlFeature>();
        return services.BuildServiceProvider();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Throws<TException>(Action action)
        where TException : Exception
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

    private sealed class ProbeDependency
    {
        public int FeaturesCreated
        {
            get; set;
        }
        public int ControlsCreated
        {
            get; set;
        }
        public bool FailFeatureConstruction
        {
            get; set;
        }
        public bool FailSettingsInitialization
        {
            get; set;
        }
        public bool FailControlConstruction
        {
            get; set;
        }
        public bool FailControlInitialization
        {
            get; set;
        }
    }

    private sealed class GeneralSettings;
    private sealed class GeneralSettingsControl : ControlWithSettingsBase<GeneralSettings>
    {
        public int SettingsCallbacks
        {
            get; private set;
        }

        protected override void OnSettingsChanged() => SettingsCallbacks++;
    }

    private sealed class ProbeSettings : IFeatureSettings;
    private sealed class OtherSettings : IFeatureSettings;
    private abstract class TestFeature<TTarget> : FeatureBase<TTarget> where TTarget : class
    {
        public override void Attach(TTarget target) => Target = target;
        public override void Detach() => Target = null;
    }

    private abstract class IntermediateFeature<TTarget> : FeatureBase<TTarget, ProbeSettings> where TTarget : class
    {
        public override void Attach(TTarget target) => Target = target;
        public override void Detach() => Target = null;
    }

    [FeatureInfo("test.without-control", "Without control")]
    private sealed class WithoutControlFeature : IntermediateFeature<object>;
    [FeatureInfo("test.probe", "Probe", "\U000F07F0", "Probe description")]
    private sealed class ProbeFeature : IntermediateFeature<UserControl>
    {
        public ProbeFeature(ProbeDependency dependency)
        {
            if (dependency.FailFeatureConstruction)
            {
                throw new InvalidOperationException("Feature construction failed.");
            }

            Dependency = dependency;
            dependency.FeaturesCreated++;
            PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(Settings))
                {
                    SettingsNotifications++;
                }
            };
        }

        public ProbeDependency Dependency
        {
            get;
        }
        public int SettingsNotifications
        {
            get; private set;
        }
        public int SettingsCallbacks
        {
            get; private set;
        }

        protected override void OnSettingsChanged()
        {
            if (Dependency.FailSettingsInitialization)
            {
                throw new InvalidOperationException("Settings initialization failed.");
            }

            SettingsCallbacks++;
        }
    }

    private abstract class IntermediateSettingsControl<TSettings> : FeatureSettingsControlBase<TSettings> where TSettings : class;
    private sealed class ProbeSettingsControl : IntermediateSettingsControl<ProbeSettings>
    {
        public ProbeSettingsControl(ProbeDependency dependency)
        {
            if (dependency.FailControlConstruction)
            {
                throw new InvalidOperationException("Settings control construction failed.");
            }

            Dependency = dependency;
            dependency.ControlsCreated++;
            PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(Settings))
                {
                    SettingsNotifications++;
                }
            };
        }

        public ProbeDependency Dependency
        {
            get;
        }
        public int SettingsNotifications
        {
            get; private set;
        }
        public int SettingsCallbacks
        {
            get; private set;
        }

        protected override void OnSettingsChanged()
        {
            if (Dependency.FailControlInitialization)
            {
                throw new InvalidOperationException("Settings control initialization failed.");
            }

            if (SettingsInternal != null)
            {
                Check(ReferenceEquals(Settings, SettingsInternal), "The typed model was unavailable during initialization.");
            }
            SettingsCallbacks++;
        }
    }

    private sealed class PlainSettingsControl : FeatureSettingsControlBase;
    private sealed class WrongSettingsControl : FeatureSettingsControlBase<OtherSettings>;
    [FeatureInfo("test.plain", "Plain", "Plain description")]
    private class PlainFeature : TestFeature<object>;
    [FeatureInfo("test.probe", "Duplicate")]
    private sealed class DuplicateFeature : TestFeature<object>;
    private sealed class MissingInfoFeature : TestFeature<object>;
    [FeatureInfo("test.abstract", "Abstract")]
    private abstract class AbstractFeature : TestFeature<object>;
    [FeatureInfo(" ", "Empty ID")]
    private sealed class EmptyIdFeature : TestFeature<object>;
    [FeatureInfo("test.empty-name", " ")]
    private sealed class EmptyNameFeature : TestFeature<object>;
    private sealed class InheritedInfoFeature : PlainFeature;
    [FeatureInfo("test.invalid", "Invalid")]
    private sealed class NotAFeature;
    private abstract class AbstractSettingsControl : FeatureSettingsControlBase;
}
