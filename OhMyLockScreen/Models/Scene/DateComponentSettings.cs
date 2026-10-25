using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Serialization;

namespace OhMyLockScreen.Models.Scene;

[AutoJsonTypeName("omls.date")]
public partial class DateComponentSettings : ObservableObject, IComponentSettings;
