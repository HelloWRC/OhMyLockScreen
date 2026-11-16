using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using Windows.UI.Xaml;

namespace OhMyLockScreen.Controls.Components;

public sealed class WebView2ComponentDefaults : IComponentDefaultFeaturesProvider
{
    public IEnumerable<FeatureInfo> CreateDefaultFeatures() =>
    [
        new FeatureInfo
        {
            Id = "omls.features.layout.layout",
            Settings = new LayoutFeatureSettings
            {
                Width = 640,
                Height = 360,
                IsAutoWidth = false,
                IsAutoHeight = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        }
    ];
}
