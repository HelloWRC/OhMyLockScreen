using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;

namespace OhMyLockScreen.Controls.Components;
// UWP XAML roots require concrete, non-generic type names.
public abstract class TextComponentBase : ComponentBase<TextComponentSettings>;
public abstract class ClockComponentBase : ComponentBase<ClockComponentSettings>;
public abstract class DateComponentBase : ComponentBase<DateComponentSettings>;
public abstract class ContainerComponentBase : ComponentBase<ContainerComponentSettings>;
public abstract class TextSettingsControlBase : ControlWithSettingsBase<TextComponentSettings>;
public abstract class ClockSettingsControlBase : ControlWithSettingsBase<ClockComponentSettings>;
public abstract class ContainerSettingsControlBase : ControlWithSettingsBase<ContainerComponentSettings>;
