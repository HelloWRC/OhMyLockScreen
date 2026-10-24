using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;

namespace OhMyLockScreen.Models.Features.Layout;

public partial class GridLayoutFeatureSettings : ObservableObject, IFeatureSettings
{
    [ObservableProperty][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] private int _row;
    [ObservableProperty][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] private int _column;
    [ObservableProperty][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] private int _rowSpan = 1;
    [ObservableProperty][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] private int _columnSpan = 1;
}
