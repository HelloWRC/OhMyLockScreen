using Microsoft.Extensions.DependencyInjection;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Services.Scene;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

internal static class ComponentDefaultFeaturesTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Default feature registration stores provider metadata without creating providers or controls", () =>
        {
            var providersCreated = DefaultFeaturesProvider.Created;
            var componentsCreated = DefaultFeaturesComponent.Created;
            var services = new ServiceCollection();
            services.AddComponent<DefaultFeaturesComponent>();
            using var provider = services.BuildServiceProvider();
            var info = provider.GetRequiredService<ComponentRegistryService>().Find(DefaultFeaturesComponent.Id)!;
            Check(info.DefaultFeaturesProviderType == typeof(DefaultFeaturesProvider)
                && DefaultFeaturesProvider.Created == providersCreated
                && DefaultFeaturesComponent.Created == componentsCreated,
                "Registration lost the provider type or instantiated a provider or component.");
            _ = info.CreateSettings();
            Check(DefaultFeaturesProvider.Created == providersCreated + 1
                && DefaultFeaturesComponent.Created == componentsCreated,
                "Creating configuration did not construct a provider independently of the control.");
        });
        run("Invalid default feature providers fail registration without changing services", () =>
        {
            var services = new ServiceCollection();
            var count = services.Count;
            Throws<ArgumentException>(() => services.AddComponent<WrongProviderComponent>());
            Throws<ArgumentException>(() => services.AddComponent<AbstractProviderComponent>());
            Throws<ArgumentException>(() => services.AddComponent<OpenProviderComponent>());
            Throws<ArgumentException>(() => services.AddComponent<InterfaceProviderComponent>());
            Throws<ArgumentException>(() => services.AddComponent<NoDefaultConstructorComponent>());
            Check(services.Count == count, "Invalid provider registration partially changed services.");
            Throws<ArgumentNullException>(() => _ = new ComponentDefaultFeaturesAttribute(null!));
        });
        run("New component defaults preserve feature order and own all mutable configuration", () =>
        {
            var services = new ServiceCollection();
            services.AddComponent<DefaultFeaturesComponent>();
            using var provider = services.BuildServiceProvider();
            var info = provider.GetRequiredService<ComponentRegistryService>().Find(DefaultFeaturesComponent.Id)!;
            var first = info.CreateSettings();
            var second = info.CreateSettings();
            Check(first.Id == DefaultFeaturesComponent.Id && first.Settings is TextComponentSettings
                && !ReferenceEquals(first.Settings, second.Settings)
                && !ReferenceEquals(first.Features, second.Features)
                && first.Features.Select(feature => feature.Id).SequenceEqual(
                    ["omls.features.appearance.border", "omls.features.layout.layout"]),
                "Component defaults lost their settings, independence or declaration order.");
            for (var index = 0; index < first.Features.Count; index++)
            {
                Check(!ReferenceEquals(first.Features[index], second.Features[index])
                    && !ReferenceEquals(first.Features[index].Settings, second.Features[index].Settings),
                    "New components share a feature or its settings.");
            }

            var firstBorder = first.Features[0].GetSettings<BorderFeatureSettings>();
            var secondBorder = second.Features[0].GetSettings<BorderFeatureSettings>();
            Check(firstBorder.BorderThickness == new Thickness(2) && firstBorder.CornerRadius == new CornerRadius(8)
                && !ReferenceEquals(firstBorder.BorderBrush, secondBorder.BorderBrush),
                "Initial border values were lost or brushes were shared.");
            firstBorder.BorderThickness = new Thickness(9);
            ((SolidColorBrush)firstBorder.BorderBrush!).Color = Color.FromArgb(255, 90, 80, 70);
            first.Features[1].GetSettings<LayoutFeatureSettings>().Margin = new Thickness(99);
            first.Features.Clear();
            Check(secondBorder.BorderThickness == new Thickness(2)
                && secondBorder.BorderBrush is SolidColorBrush { Color.R: 12 }
                && second.Features.Count == 2
                && second.Features[1].GetSettings<LayoutFeatureSettings>().Margin == new Thickness(1, 2, 3, 4),
                "Editing one component changed another component's defaults.");
        });
        run("Components without declared defaults retain their settings and empty feature lists", () =>
        {
            var services = new ServiceCollection();
            services.AddComponent<PlainComponent>();
            services.AddComponent<EmptyDefaultsComponent>();
            using var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<ComponentRegistryService>();
            var text = registry.Find("test.plain")!;
            var settings = text.CreateSettings();
            Check(text.DefaultFeaturesProviderType == null && settings.Id == "test.plain"
                && settings.Settings is TextComponentSettings && settings.Features.Count == 0,
                "Undeclared defaults changed existing component configuration.");
            var empty = registry.Find("test.empty-defaults")!.CreateSettings();
            Check(empty.Settings == null && empty.Features.Count == 0,
                "An empty provider or component without custom settings could not create configuration.");
        });
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

    [ComponentInfo(Id, "Default features")]
    [ComponentDefaultFeatures(typeof(DefaultFeaturesProvider))]
    public sealed class DefaultFeaturesComponent : ComponentBase<TextComponentSettings>
    {
        public const string Id = "test.default-features";
        public static int Created { get; private set; }
        public DefaultFeaturesComponent() => Created++;
    }

    public sealed class DefaultFeaturesProvider : IComponentDefaultFeaturesProvider
    {
        public static int Created { get; private set; }
        public DefaultFeaturesProvider() => Created++;

        public IEnumerable<FeatureInfo> CreateDefaultFeatures() =>
        [
            new FeatureInfo
            {
                Id = "omls.features.appearance.border",
                Settings = new BorderFeatureSettings
                {
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(8),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 12, 34, 56))
                }
            },
            new FeatureInfo
            {
                Id = "omls.features.layout.layout",
                Settings = new LayoutFeatureSettings
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(1, 2, 3, 4)
                }
            }
        ];
    }

    [ComponentInfo(Id, "Appearance defaults")]
    [ComponentDefaultFeatures(typeof(AppearanceDefaultsProvider))]
    public sealed class AppearanceDefaultsComponent : ComponentBase
    {
        public const string Id = "test.appearance-defaults";
    }

    public sealed class AppearanceDefaultsProvider : IComponentDefaultFeaturesProvider
    {
        public IEnumerable<FeatureInfo> CreateDefaultFeatures() =>
        [
            new FeatureInfo
            {
                Id = "omls.features.appearance.foreground",
                Settings = new ForegroundFeatureSettings()
            }
        ];
    }

    [ComponentInfo(Id, "Failing defaults")]
    [ComponentDefaultFeatures(typeof(FailingDefaultsProvider))]
    public sealed class FailingDefaultsComponent : ComponentBase
    {
        public const string Id = "test.failing-defaults";
    }

    public sealed class FailingDefaultsProvider : IComponentDefaultFeaturesProvider
    {
        public IEnumerable<FeatureInfo> CreateDefaultFeatures()
        {
            yield return new FeatureInfo { Id = "omls.features.layout.layout" };
            throw new InvalidOperationException("Simulated default feature enumeration failure.");
        }
    }

    [ComponentInfo("test.plain", "Plain component")]
    private sealed class PlainComponent : ComponentBase<TextComponentSettings>;

    [ComponentInfo("test.empty-defaults", "Empty defaults")]
    [ComponentDefaultFeatures(typeof(EmptyDefaultsProvider))]
    private sealed class EmptyDefaultsComponent : ComponentBase;

    public sealed class EmptyDefaultsProvider : IComponentDefaultFeaturesProvider
    {
        public IEnumerable<FeatureInfo> CreateDefaultFeatures() => [];
    }

    [ComponentInfo("test.wrong-provider", "Wrong provider")]
    [ComponentDefaultFeatures(typeof(object))]
    private sealed class WrongProviderComponent : ComponentBase;

    [ComponentInfo("test.abstract-provider", "Abstract provider")]
    [ComponentDefaultFeatures(typeof(AbstractProvider))]
    private sealed class AbstractProviderComponent : ComponentBase;

    [ComponentInfo("test.open-provider", "Open provider")]
    [ComponentDefaultFeatures(typeof(OpenProvider<>))]
    private sealed class OpenProviderComponent : ComponentBase;

    [ComponentInfo("test.interface-provider", "Interface provider")]
    [ComponentDefaultFeatures(typeof(IComponentDefaultFeaturesProvider))]
    private sealed class InterfaceProviderComponent : ComponentBase;

    [ComponentInfo("test.no-default-constructor", "No default constructor")]
    [ComponentDefaultFeatures(typeof(NoDefaultConstructorProvider))]
    private sealed class NoDefaultConstructorComponent : ComponentBase;

    public abstract class AbstractProvider : IComponentDefaultFeaturesProvider
    {
        public abstract IEnumerable<FeatureInfo> CreateDefaultFeatures();
    }

    public sealed class OpenProvider<T> : IComponentDefaultFeaturesProvider
    {
        public IEnumerable<FeatureInfo> CreateDefaultFeatures() => [];
    }

    public sealed class NoDefaultConstructorProvider : IComponentDefaultFeaturesProvider
    {
        private NoDefaultConstructorProvider() { }
        public IEnumerable<FeatureInfo> CreateDefaultFeatures() => [];
    }
}
