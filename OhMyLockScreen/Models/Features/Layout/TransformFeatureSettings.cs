using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;

namespace OhMyLockScreen.Models.Features.Layout;

public partial class TransformFeatureSettings : ObservableObject, IFeatureSettings
{
    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    private double _translateX;

    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    private double _translateY;

    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    private double _rotation;

    [ObservableProperty]
    private double _scaleX = 1;

    [ObservableProperty]
    private double _scaleY = 1;

    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    private bool _flipHorizontal;

    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    private bool _flipVertical;

    [ObservableProperty]
    private bool _isScaleLocked = true;

    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    private double _skewX;

    [ObservableProperty]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    private double _skewY;

    [ObservableProperty]
    private double _originX = 0.5;

    [ObservableProperty]
    private double _originY = 0.5;
}
