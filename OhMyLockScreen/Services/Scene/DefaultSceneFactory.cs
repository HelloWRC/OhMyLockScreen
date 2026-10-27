using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Services.Scene;

internal static class DefaultSceneFactory
{
    internal static SceneSettings Create(string name) => new()
    {
        Name = name,
        Layers =
        {
            new LayerSettings
            {
                Name = "ContentLayer",
                Kind = LayoutKind.Grid,
                Children =
                {
                    new ComponentSettings
                    {
                        Id = "omls.components.container",
                        Name = "Clock",
                        Settings = new ContainerComponentSettings
                        {
                            Children =
                            {
                                TextComponent(ClockComponent.Id, new ClockComponentSettings(), new ClockDefaultFeaturesProvider()),
                                TextComponent(DateComponent.Id, new DateComponentSettings(), new DateDefaultFeaturesProvider())
                            }
                        },
                        Features =
                        {
                            Layout(HorizontalAlignment.Center, new Thickness(0, 64, 0, 0))
                        }
                    }
                }
            }
        }
    };

    private static ComponentSettings TextComponent(string id, IComponentSettings settings, IComponentDefaultFeaturesProvider defaults)
    {
        var component = new ComponentSettings { Id = id, Settings = settings };
        foreach (var feature in defaults.CreateDefaultFeatures())
        {
            component.Features.Add(feature);
        }

        component.Features.Add(Layout(HorizontalAlignment.Center));
        return component;
    }

    private static FeatureInfo Layout(HorizontalAlignment alignment, Thickness margin = default) => new()
    {
        Id = "omls.features.layout.layout",
        Settings = new LayoutFeatureSettings
        {
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = margin
        }
    };
}
