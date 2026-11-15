using CommunityToolkit.Mvvm.ComponentModel;

namespace OhMyLockScreen.Shared.Models.Scene;

public partial class BackgroundSettings : ObservableObject
{
    private const double DefaultParallaxStrength = 100;

    [ObservableProperty]
    private BackgroundMode _mode;
    [ObservableProperty]
    private string? _blobId;
    [ObservableProperty]
    private string? _imageBlobId;
    [ObservableProperty]
    private bool _enableSound;

    private double _parallaxStrength = DefaultParallaxStrength;

    public double ParallaxStrength
    {
        get => _parallaxStrength;
        set => SetProperty(ref _parallaxStrength, double.IsFinite(value)
            ? Math.Clamp(value, 0, 200)
            : DefaultParallaxStrength);
    }
}

public enum BackgroundMode
{
    None,
    Video,
    Image
}
