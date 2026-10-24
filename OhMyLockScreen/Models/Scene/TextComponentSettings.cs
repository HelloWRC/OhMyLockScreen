using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Serialization;

namespace OhMyLockScreen.Models.Scene;

[AutoJsonTypeName("omls.text")]
public partial class TextComponentSettings : ObservableObject, IComponentSettings
{
    [ObservableProperty] private string _text = "Hello, OhMyLockScreen!";

    [ObservableProperty] private double _fontSize = 32;
}
