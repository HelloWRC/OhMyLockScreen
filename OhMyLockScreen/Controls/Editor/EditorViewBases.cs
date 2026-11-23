using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Features.Layout;
using OhMyLockScreen.Shared.Abstractions.Controls;

namespace OhMyLockScreen.Controls.Editor;

public abstract class LayoutSettingsControlBase : FeatureSettingsControlBase<LayoutFeatureSettings>;
public abstract class GridLayoutSettingsControlBase : FeatureSettingsControlBase<GridLayoutFeatureSettings>;
public abstract class TransformSettingsControlBase : FeatureSettingsControlBase<TransformFeatureSettings>;
public abstract class ParallaxSettingsControlBase : FeatureSettingsControlBase<ParallaxFeatureSettings>;
public abstract class BackgroundSettingsControlBase : FeatureSettingsControlBase<BackgroundFeatureSettings>;
public abstract class BorderSettingsControlBase : FeatureSettingsControlBase<BorderFeatureSettings>;
public abstract class ShadowSettingsControlBase : FeatureSettingsControlBase<ShadowFeatureSettings>;
public abstract class ForegroundSettingsControlBase : FeatureSettingsControlBase<ForegroundFeatureSettings>;
public abstract class TextFeatureSettingsControlBase : FeatureSettingsControlBase<TextFeatureSettings>;
public abstract class FontSettingsControlBase : FeatureSettingsControlBase<FontFeatureSettings>;
