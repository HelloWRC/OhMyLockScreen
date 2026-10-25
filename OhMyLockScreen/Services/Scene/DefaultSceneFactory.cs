using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Models.Scene;
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
                                new ComponentSettings
                                {
                                    Id = "omls.components.clock",
                                    Settings = new ClockComponentSettings(),
                                    Features =
                                    {
                                        Layout(HorizontalAlignment.Center)
                                    }
                                },
                                new ComponentSettings
                                {
                                    Id = "omls.components.date",
                                    Settings = new DateComponentSettings(),
                                    Features =
                                    {
                                        Layout(HorizontalAlignment.Center)
                                    }
                                }
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
