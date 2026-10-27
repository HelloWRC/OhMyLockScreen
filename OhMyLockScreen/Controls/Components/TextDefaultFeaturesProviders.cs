using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;

namespace OhMyLockScreen.Controls.Components;

public sealed class TextDefaultFeaturesProvider : IComponentDefaultFeaturesProvider
{
    public IEnumerable<FeatureInfo> CreateDefaultFeatures() => [TextDefaults.Size(32)];
}

public sealed class ClockDefaultFeaturesProvider : IComponentDefaultFeaturesProvider
{
    public IEnumerable<FeatureInfo> CreateDefaultFeatures() =>
    [
        TextDefaults.Size(64),
        new FeatureInfo
        {
            Id = FontFeature.Id,
            Settings = new FontFeatureSettings { FontWeight = 700 }
        }
    ];
}

public sealed class DateDefaultFeaturesProvider : IComponentDefaultFeaturesProvider
{
    public IEnumerable<FeatureInfo> CreateDefaultFeatures() => [TextDefaults.Size(24)];
}

internal static class TextDefaults
{
    internal static FeatureInfo Size(double size) => new()
    {
        Id = TextFeature.Id,
        Settings = new TextFeatureSettings { FontSize = size }
    };
}
