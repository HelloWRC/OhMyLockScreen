using System.Text.Json.Serialization;
using Windows.UI.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;

namespace OhMyLockScreen.Models.Features.Layout;

public partial class LayoutFeatureSettings : ObservableObject, IFeatureSettings
{
    [ObservableProperty][property:JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] private double _width;
    [ObservableProperty][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] private double _height;
    [ObservableProperty][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] private bool _isAutoWidth = true;
    [ObservableProperty][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] private bool _isAutoHeight = true;
    [ObservableProperty][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] private VerticalAlignment _verticalAlignment;
    [ObservableProperty][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] private HorizontalAlignment _horizontalAlignment;
    [ObservableProperty][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] private Thickness _margin;
}