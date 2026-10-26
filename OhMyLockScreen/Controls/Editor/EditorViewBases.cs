using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.Controls;

namespace OhMyLockScreen.Controls.Editor;

public abstract class LayoutSettingsControlBase : FeatureSettingsControlBase<LayoutFeatureSettings>;
public abstract class GridLayoutSettingsControlBase : FeatureSettingsControlBase<GridLayoutFeatureSettings>;
public abstract class BackgroundSettingsControlBase : FeatureSettingsControlBase<BackgroundFeatureSettings>;
public abstract class ForegroundSettingsControlBase : FeatureSettingsControlBase<ForegroundFeatureSettings>;
