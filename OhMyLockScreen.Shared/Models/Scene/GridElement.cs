using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OhMyLockScreen.Shared.Models.Scene;

public partial class GridElement : ObservableObject
{
    [ObservableProperty] private GridElementMode _mode;
    [ObservableProperty] private double _value = 1;
    [ObservableProperty] private double _minValue;
    [ObservableProperty]
    [property: JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    private double _maxValue = double.PositiveInfinity;
}

public enum GridElementMode
{
    Star,
    Fixed,
    Auto
}
