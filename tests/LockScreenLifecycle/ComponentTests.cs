using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLockScreen.Controls.Scene;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Attributes.Serialization;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Helpers.Serialization;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

internal static class ComponentTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Component registration validates metadata and container contracts without partial registration", () =>
        {
            var services = new ServiceCollection();
            services.AddComponent<TextComponent>();
            var count = services.Count;
            Throws<ArgumentException>(() => services.AddComponent<TextComponent>());
            Throws<ArgumentException>(() => services.AddComponent<DuplicateComponent>());
            Throws<ArgumentException>(() => services.AddComponent<MissingInfoComponent>());
            Throws<ArgumentException>(() => services.AddComponent<AbstractComponent>());
            Throws<ArgumentException>(() => services.AddComponent<EmptyIdComponent>());
            Throws<ArgumentException>(() => services.AddComponent<MissingContainerSettingsComponent>());
            Throws<ArgumentException>(() => services.AddComponent<MissingContainerMarkerComponent>());
            Check(services.Count == count, "Failed registration modified the service collection.");
            services.AddComponent<TestContainerComponent>();
            using var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<ComponentRegistryService>();
            Check(registry.Registered.Count == 2, "Unexpected registered components.");
            var text = registry.Find(TextComponent.Id)!;
            Check(text.ComponentType == typeof(TextComponent) && text.ClientSettingsType == typeof(TextComponentSettings)
                && text.SettingsType == null
                && text.Name == "文本" && !text.IsComponentContainer, "Text metadata was lost.");
            Check(registry.Find("test.container")!.IsComponentContainer, "Container was not marked.");
        });

        run("Component font icons preserve glyphs and use independent FluentIcons sources", () =>
        {
            var noIcon = new ComponentInfoAttribute("test.no-icon", "No icon");
            var descriptionOnly = new ComponentInfoAttribute("test.description", "Description", "Description only");
            Check(noIcon.IconSource == null && descriptionOnly.IconSource == null
                && descriptionOnly.Description == "Description only", "Icon support broke metadata without icons.");
            var emptyIcon = new ComponentInfoAttribute("test.empty-icon", "Empty icon", "", "Description");
            Check(emptyIcon.IconSource == null && emptyIcon.Description == "Description", "Empty glyph created an icon.");
            const string glyph = "\U000F07F0";
            var info = new ComponentInfoAttribute("test.icon", "Icon", glyph, "Icon description");
            var first = info.IconSource!;
            var second = info.IconSource!;
            Check(info.IconGlyph == glyph && first.Glyph == glyph && second.Glyph == glyph
                && !ReferenceEquals(first, second), "Supplementary glyphs were changed or icon sources were shared.");
            Check(first.FontFamily!.Source == new FluentIcons.Uwp.FluentIconSource().FontFamily!.Source,
                "Icon did not use the default FluentIcons font.");
            first.Glyph = "changed";
            Check(info.IconSource!.Glyph == glyph, "Changing one icon source changed stored metadata.");

            var services = new ServiceCollection();
            services.AddComponent<TextComponent>();
            services.AddComponent<PlainComponent>();
            using var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<ComponentRegistryService>();
            var text = registry.Find(TextComponent.Id)!;
            Check(text.IconGlyph == glyph && text.Description == "显示自定义文本。"
                && text.IconSource!.Glyph == glyph && !ReferenceEquals(text.IconSource, text.IconSource),
                "Registration lost or cached the icon.");
            Check(registry.Find("test.plain")!.IconSource == null, "Registration added an icon to an iconless component.");
        });

        run("Settings control registration stays separate from the component settings model", () =>
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ProbeDependency>();
            services.AddSingleton<IComponentsService, ComponentsService>();
            var count = services.Count;
            Throws<ArgumentException>(() => services.AddComponent<TextComponent, AbstractSettingsControl>());
            Check(services.Count == count, "Invalid settings control partially registered the component.");
            services.AddComponent<TextComponent, TestSettingsControl>();
            using var provider = services.BuildServiceProvider();
            var info = provider.GetRequiredService<ComponentRegistryService>().Find(TextComponent.Id)!;
            Check(info.SettingsType == typeof(TestSettingsControl)
                && info.ClientSettingsType == typeof(TextComponentSettings), "Settings control and model types were confused.");
            var settings = new ComponentSettings { Id = TextComponent.Id };
            var component = (TextComponent)provider.GetRequiredService<IComponentsService>().CreateComponent(settings)!;
            var dependency = provider.GetRequiredService<ProbeDependency>();
            Check(settings.Settings is TextComponentSettings && ReferenceEquals(settings.Settings, component.Settings)
                && dependency.SettingsControlsCreated == 0, "Component creation instantiated the settings control as a model.");
            var first = provider.GetRequiredService<TestSettingsControl>();
            var second = provider.GetRequiredService<TestSettingsControl>();
            Check(!ReferenceEquals(first, second) && ReferenceEquals(first.Dependency, dependency)
                && dependency.SettingsControlsCreated == 2, "Settings controls did not use transient constructor injection.");
        });

        run("Component registries belong to individual service providers", () =>
        {
            var first = new ServiceCollection();
            first.AddComponent<TextComponent>();
            var second = new ServiceCollection();
            second.AddComponent<PlainComponent>();
            using var firstProvider = first.BuildServiceProvider();
            using var secondProvider = second.BuildServiceProvider();
            var firstRegistry = firstProvider.GetRequiredService<ComponentRegistryService>();
            var secondRegistry = secondProvider.GetRequiredService<ComponentRegistryService>();
            Check(ReferenceEquals(firstRegistry, firstProvider.GetRequiredService<ComponentRegistryService>()),
                "Registry was not a singleton.");
            Check(firstRegistry.Find("test.plain") == null && secondRegistry.Find(TextComponent.Id) == null,
                "Independent hosts shared registration state.");
        });

        run("Component creation injects dependencies and initializes typed settings once", () =>
        {
            var host = CreateHost();
            var factory = host.Services.GetRequiredService<IComponentsService>();
            var settings = new ComponentSettings { Id = "test.probe" };
            var first = (ProbeComponent)factory.CreateComponent(settings)!;
            var second = (ProbeComponent)factory.CreateComponent(settings)!;
            var dependency = host.Services.GetRequiredService<ProbeDependency>();
            Check(!ReferenceEquals(first, second) && dependency.Created == 2, "Component views were shared.");
            Check(ReferenceEquals(first.Dependency, dependency), "Constructor dependency was not injected.");
            Check(settings.Settings is TextComponentSettings && ReferenceEquals(first.Settings, settings.Settings)
                && ReferenceEquals(first.Settings, second.Settings), "Settings were not initialized or reused.");
            Check(first.SettingsNotifications == 1 && second.SettingsNotifications == 1,
                "Settings initialization did not notify bindings exactly once.");
            Check(factory.CreateComponent(new ComponentSettings { Id = "test.plain" }) is PlainComponent,
                "A component without custom settings could not be created.");
        });

        run("Creation failures preserve configuration and unknown IDs return empty", () =>
        {
            var factory = CreateHost().Services.GetRequiredService<IComponentsService>();
            var original = new TestContainerSettings();
            var settings = new ComponentSettings { Id = TextComponent.Id, Settings = original };
            Throws<InvalidOperationException>(() => factory.CreateComponent(settings));
            Check(ReferenceEquals(original, settings.Settings), "Mismatched settings were overwritten.");
            var failed = new ComponentSettings { Id = "test.failing" };
            Throws<InvalidOperationException>(() => factory.CreateComponent(failed));
            Check(failed.Settings == null, "Constructor failure committed default settings.");
            var failedInitialization = new ComponentSettings { Id = "test.failing-initialization" };
            Throws<InvalidOperationException>(() => factory.CreateComponent(failedInitialization));
            Check(failedInitialization.Settings == null, "Initialization failure committed default settings.");
            Check(factory.CreateComponent(new ComponentSettings { Id = "unknown", Settings = original }) == null,
                "An unknown ID did not return empty.");
        });

        run("Layout containers own separate component content across preparation and panel changes", () =>
        {
            var host = CreateHost();
            var dependency = host.Services.GetRequiredService<ProbeDependency>();
            var settings = new ComponentSettings { Id = "test.probe" };
            var layoutSettings = new LayerSettings();
            var layout = new LayoutControl { Settings = layoutSettings };
            layout.Load();
            var first = (ComponentPresenter)layout.CreateItemContainer(settings);
            var second = (ComponentPresenter)layout.CreateItemContainer(settings);
            layout.PrepareItemContainer(first, settings);
            layout.PrepareItemContainer(second, settings);
            Check(!ReferenceEquals(first, second) && first.Content == null && second.Content == null,
                "Container creation shared presenters or created content before loading.");
            Check(ReferenceEquals(first.DataContext, settings) && ReferenceEquals(first.Settings, settings),
                "Container preparation did not assign the item context and settings.");
            first.Load();
            second.Load();
            var firstContent = first.Content;
            var secondContent = second.Content;
            Check(firstContent is ProbeComponent && secondContent is ProbeComponent
                && !ReferenceEquals(firstContent, secondContent) && dependency.Created == 2,
                "Containers did not own independent component controls.");

            foreach (var kind in new[] { LayoutKind.Canvas, LayoutKind.StackPanel, LayoutKind.WrapPanel, LayoutKind.Grid })
            {
                layoutSettings.Kind = kind;
                layout.PrepareItemContainer(first, settings);
                layout.PrepareItemContainer(second, settings);
                Check(ReferenceEquals(first.Content, firstContent) && ReferenceEquals(second.Content, secondContent),
                    "Repeated preparation overwrote component content with the item.");
            }

            layout.ClearItemContainer(first, settings);
            Check(first.Settings == null && first.DataContext == null && first.Content == null,
                "Clearing a loaded container retained item references or component content.");
            settings.Settings = new TextComponentSettings();
            Check(dependency.Created == 3 && first.Content == null && second.Content is ProbeComponent,
                "Clearing one container left it subscribed or detached another container.");
            layout.ClearItemContainer(second, settings);
            first.Unload();
            second.Unload();
            layout.Unload();
        });

        run("Reassigned and cleared layout containers ignore detached models and queued changes", () =>
        {
            var host = CreateHost();
            var dependency = host.Services.GetRequiredService<ProbeDependency>();
            var layout = new LayoutControl();
            var oldSettings = new ComponentSettings { Id = "test.probe" };
            var settings = new ComponentSettings { Id = "test.probe" };
            var presenter = (ComponentPresenter)layout.CreateItemContainer(oldSettings);
            layout.PrepareItemContainer(presenter, oldSettings);
            presenter.Load();
            var owner = Window.Current;
            Window.Current = new Window();
            oldSettings.Settings = new TextComponentSettings();
            Window.Current = owner;
            layout.PrepareItemContainer(presenter, settings);
            var content = presenter.Content;
            presenter.Dispatcher.Drain();
            oldSettings.Settings = new TextComponentSettings();
            Check(dependency.Created == 2 && ReferenceEquals(presenter.Content, content)
                && ReferenceEquals(presenter.DataContext, settings) && ReferenceEquals(presenter.Settings, settings),
                "Reassignment retained an old subscription or applied an old queued update.");

            Window.Current = new Window();
            settings.Settings = new TextComponentSettings();
            Window.Current = owner;
            layout.ClearItemContainer(presenter, settings);
            presenter.Dispatcher.Drain();
            settings.Settings = new TextComponentSettings();
            Check(dependency.Created == 2 && presenter.Content == null && presenter.Settings == null
                && presenter.DataContext == null, "Cleared container recreated detached content.");
            presenter.Unload();
            layout.PrepareItemContainer(presenter, settings);
            Check(presenter.Content == null, "Reusing an unloaded container created content prematurely.");
            presenter.Load();
            Check(dependency.Created == 3 && presenter.Content is ProbeComponent,
                "A reused container did not create content on reload.");
            presenter.Unload();
            layout.ClearItemContainer(presenter, settings);
        });

        run("Existing component presenters are used directly and retain their own configuration", () =>
        {
            var host = CreateHost();
            var dependency = host.Services.GetRequiredService<ProbeDependency>();
            var layout = new LayoutControl();
            var settings = new ComponentSettings { Id = "test.probe" };
            var context = new object();
            var presenter = new ComponentPresenter { Settings = settings, DataContext = context };
            var container = layout.CreateItemContainer(presenter);
            Check(ReferenceEquals(container, presenter), "Existing presenter received an extra container.");
            layout.PrepareItemContainer(container, presenter);
            presenter.Load();
            var content = presenter.Content;
            layout.ClearItemContainer(container, presenter);
            Check(ReferenceEquals(presenter.Settings, settings) && ReferenceEquals(presenter.DataContext, context)
                && ReferenceEquals(presenter.Content, content) && content is ProbeComponent,
                "Container preparation or clearing overwrote an existing presenter's configuration.");
            presenter.Unload();
            settings.Settings = new TextComponentSettings();
            Check(dependency.Created == 1 && presenter.Content == null,
                "Existing presenter did not detach on unload.");
        });

        run("Presenters create separate controls and default-settings writeback does not recurse", () =>
        {
            var host = CreateHost();
            var dependency = host.Services.GetRequiredService<ProbeDependency>();
            var settings = new ComponentSettings { Id = "test.probe" };
            var first = new ComponentPresenter { Settings = settings };
            var second = new ComponentPresenter { Settings = settings };
            Check(first.Content == null && dependency.Created == 0, "Presenter created before loading.");
            first.Load();
            first.Load();
            second.Load();
            Check(first.Content is ProbeComponent && second.Content is ProbeComponent
                && !ReferenceEquals(first.Content, second.Content) && dependency.Created == 2,
                "Loading or default writeback duplicated or shared controls.");
            var replacement = new TextComponentSettings { Text = "replacement" };
            settings.Settings = replacement;
            Check(dependency.Created == 4 && ReferenceEquals(((ProbeComponent)first.Content!).Settings, replacement),
                "Replacing custom settings did not update both presenters exactly once.");
            first.Unload();
            second.Unload();
            settings.Settings = new TextComponentSettings();
            Check(dependency.Created == 4 && first.Content == null && second.Content == null,
                "Unloaded presenters retained their subscriptions or content.");
            first.Load();
            Check(dependency.Created == 5, "Reload did not create a fresh control.");
            first.Unload();
        });

        run("Presenters switch settings, detach old models, clear failures and recover", () =>
        {
            var host = CreateHost();
            var dependency = host.Services.GetRequiredService<ProbeDependency>();
            var oldSettings = new ComponentSettings { Id = "test.probe" };
            var presenter = new ComponentPresenter { Settings = oldSettings };
            presenter.Load();
            var settings = new ComponentSettings { Id = "test.probe" };
            presenter.Settings = settings;
            var content = presenter.Content;
            oldSettings.Settings = new TextComponentSettings();
            Check(ReferenceEquals(content, presenter.Content) && dependency.Created == 2,
                "Replaced settings remained subscribed.");
            settings.Id = "unknown";
            Check(presenter.Content == null, "Unknown ID left stale content.");
            settings.Id = "test.failing";
            Check(presenter.Content == null, "Failed constructor escaped or left stale content.");
            settings.Id = "test.probe";
            Check(presenter.Content is ProbeComponent, "Presenter did not recover.");
            settings.Settings = new TestContainerSettings();
            Check(presenter.Content == null && settings.Settings is TestContainerSettings,
                "Type mismatch was not contained or destroyed settings.");
            settings.Settings = new TextComponentSettings();
            Check(presenter.Content is ProbeComponent, "Valid settings did not restore the component.");
            presenter.Settings = null;
            Check(presenter.Content == null, "Null settings did not clear the content.");
            presenter.Unload();
            presenter.Settings = new ComponentSettings { Id = TextComponent.Id };
            Check(presenter.Content == null, "Unloaded presenter created content.");
            presenter.Load();
            Check(presenter.Content is TextComponent, "Reload ignored the latest settings.");
            presenter.Unload();
        });

        run("Text example binds to observable text and font size settings", () =>
        {
            var factory = CreateHost().Services.GetRequiredService<IComponentsService>();
            var settings = new TextComponentSettings { Text = "sample", FontSize = 48 };
            var component = (TextComponent)factory.CreateComponent(new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = settings
            })!;
            var text = (TextBlock)component.Content!;
            var textBinding = text.Bindings[TextBlock.TextProperty];
            var sizeBinding = text.Bindings[TextBlock.FontSizeProperty];
            Check(ReferenceEquals(textBinding.Source, settings) && textBinding.Path!.Path == nameof(settings.Text)
                && textBinding.Mode == BindingMode.OneWay, "Text binding does not target component settings.");
            Check(ReferenceEquals(sizeBinding.Source, settings) && sizeBinding.Path!.Path == nameof(settings.FontSize),
                "Font size binding does not target component settings.");
            var notifications = new List<string?>();
            settings.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
            settings.Text = "changed";
            settings.FontSize = 72;
            Check(notifications.SequenceEqual([nameof(settings.Text), nameof(settings.FontSize)]),
                "Text settings changes did not notify bindings.");
        });

        run("Shared settings update each presenter on its owning view and ignore detached queued changes", () =>
        {
            var host = CreateHost();
            var dependency = host.Services.GetRequiredService<ProbeDependency>();
            var settings = new ComponentSettings { Id = "test.probe" };
            var firstWindow = Window.Current;
            var first = new ComponentPresenter { Settings = settings };
            first.Load();
            var secondWindow = new Window();
            Window.Current = secondWindow;
            var second = new ComponentPresenter { Settings = settings };
            second.Load();
            Window.Current = firstWindow;
            settings.Settings = new TextComponentSettings();
            Check(dependency.Created == 3 && ReferenceEquals(((ProbeComponent)first.Content!).Owner, firstWindow),
                "Settings change created another view's component on the current view.");
            Window.Current = secondWindow;
            second.Dispatcher.Drain();
            Check(dependency.Created == 4 && ReferenceEquals(((ProbeComponent)second.Content!).Owner, secondWindow),
                "Dispatched update did not run on its owning view.");
            Window.Current = firstWindow;
            settings.Settings = new TextComponentSettings();
            Window.Current = secondWindow;
            second.Unload();
            second.Dispatcher.Drain();
            Check(dependency.Created == 5 && second.Content == null, "Queued change recreated an unloaded view.");
            Window.Current = firstWindow;
            first.Unload();
        });

        run("Container contracts and nested polymorphic settings round trip through configuration helpers", () =>
        {
            var factory = CreateHost().Services.GetRequiredService<IComponentsService>();
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(IAppHost).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForAssembly(typeof(ComponentTests).Assembly);
            AutoJsonPolymorphicRegistrationHelper.DiscoverAndRegisterForCurrentAssembly();
            var root = new ComponentSettings { Id = "test.container" };
            var container = (TestContainerComponent)factory.CreateComponent(root)!;
            container.Settings.Children.Add(new ComponentSettings
            {
                Id = "test.container",
                Settings = new TestContainerSettings
                {
                    Children =
                    [
                        new ComponentSettings
                        {
                            Id = TextComponent.Id,
                            Settings = new TextComponentSettings { Text = "nested", FontSize = 56 }
                        }
                    ]
                }
            });
            var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "components.json");
            ConfigureFileHelper.SaveConfig(path, root, true);
            var restored = ConfigureFileHelper.LoadConfigUnWrapped<ComponentSettings>(path);
            var outer = restored.GetSettings<TestContainerSettings>();
            var inner = outer.Children[0].GetSettings<TestContainerSettings>();
            var text = inner.Children[0].GetSettings<TextComponentSettings>();
            Check(text.Text == "nested" && text.FontSize == 56 && File.ReadAllText(path).Contains("\"$type\""),
                "Nested interface settings did not round trip.");
            Check(!ReferenceEquals(container.Settings, outer), "Round trip shared configuration objects.");
            var restoredContainer = (TestContainerComponent)factory.CreateComponent(restored)!;
            Check(ReferenceEquals(outer, restoredContainer.Settings), "Container creation replaced its children.");
            Check(factory.CreateComponent(inner.Children[0]) is TextComponent, "Restored child could not be created.");
            var resolver = AutoJsonPolymorphicRegistrationHelper.CreateJsonTypeInfoResolver();
            var info = resolver.GetTypeInfo(typeof(IComponentSettings), new JsonSerializerOptions())!;
            Check(info.PolymorphismOptions!.DerivedTypes.All(type => !type.DerivedType.IsAbstract
                && !type.DerivedType.ContainsGenericParameters), "Discovery registered non-concrete settings.");
        });
    }

    private static IHost CreateHost()
    {
        var host = new HostBuilder()
            .ConfigureLogging(logging => logging.AddDebug())
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<ProbeDependency>();
                services.AddSingleton<FeatureRegistryService>();
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddComponent<TextComponent>();
                services.AddComponent<ProbeComponent>();
                services.AddComponent<PlainComponent>();
                services.AddComponent<FailingComponent>();
                services.AddComponent<FailingInitializationComponent>();
                services.AddComponent<TestContainerComponent>();
            })
            .Build();
        IAppHost.Host = host;
        return host;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new Exception($"Expected {typeof(T).Name}.");
    }

    public sealed class ProbeDependency
    {
        public int Created { get; set; }
        public int SettingsControlsCreated { get; set; }
    }

    public sealed class TestSettingsControl : UserControl
    {
        public ProbeDependency Dependency { get; }

        public TestSettingsControl(ProbeDependency dependency)
        {
            Dependency = dependency;
            dependency.SettingsControlsCreated++;
        }
    }

    public abstract class AbstractSettingsControl : UserControl;

    public abstract class IntermediateComponent : ComponentBase<TextComponentSettings>;

    [ComponentInfo("test.probe", "Probe")]
    public sealed class ProbeComponent : IntermediateComponent
    {
        public ProbeDependency Dependency { get; }
        public Window Owner { get; } = Window.Current;
        public int SettingsNotifications { get; private set; }

        public ProbeComponent(ProbeDependency dependency)
        {
            Dependency = dependency;
            dependency.Created++;
            PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(Settings))
                {
                    SettingsNotifications++;
                }
            };
        }
    }

    [ComponentInfo("test.plain", "Plain")]
    public sealed class PlainComponent : ComponentBase;

    [ComponentInfo(TextComponent.Id, "Duplicate")]
    public sealed class DuplicateComponent : ComponentBase;

    public sealed class MissingInfoComponent : ComponentBase;

    [ComponentInfo("test.abstract", "Abstract")]
    public abstract class AbstractComponent : ComponentBase;

    [ComponentInfo("", "Empty")]
    public sealed class EmptyIdComponent : ComponentBase;

    [ContainerComponent]
    [ComponentInfo("test.invalid-container", "Invalid")]
    public sealed class MissingContainerSettingsComponent : ComponentBase<TextComponentSettings>;

    [ComponentInfo("test.unmarked-container", "Invalid")]
    public sealed class MissingContainerMarkerComponent : ComponentBase<TestContainerSettings>;

    [ComponentInfo("test.failing", "Failing")]
    public sealed class FailingComponent : ComponentBase<TextComponentSettings>
    {
        public FailingComponent() => throw new InvalidOperationException("Simulated component creation failure.");
    }

    [ComponentInfo("test.failing-initialization", "Failing initialization")]
    public sealed class FailingInitializationComponent : ComponentBase<TextComponentSettings>
    {
        protected override void OnSettingsChanged()
            => throw new InvalidOperationException("Simulated settings initialization failure.");
    }

    [ContainerComponent]
    [ComponentInfo("test.container", "Container")]
    public sealed class TestContainerComponent : ComponentBase<TestContainerSettings>;

    [AutoJsonTypeName("test-container")]
    public sealed class TestContainerSettings : ObservableObject, IComponentContainerSettings
    {
        private ObservableCollection<ComponentSettings> _children = [];

        public ObservableCollection<ComponentSettings> Children
        {
            get => _children;
            set => SetProperty(ref _children, value);
        }
    }

    public abstract class AbstractSettings : IComponentSettings;

    public class OpenSettings<T> : IComponentSettings;
}
