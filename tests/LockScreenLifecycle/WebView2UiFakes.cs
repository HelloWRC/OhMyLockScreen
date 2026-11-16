using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Attributes.Scene;

namespace OhMyLockScreen.Controls.Components;

[ComponentInfo(Id, "网页（WebView2）")]
[ComponentDefaultFeatures(typeof(WebView2ComponentDefaults))]
public sealed class WebView2Component : ComponentBase<WebView2ComponentSettings>
{
    public const string Id = "omls.components.webView2";

    public WebView2Component() => IsHitTestVisible = false;
}

public sealed class WebView2SettingsControl : ControlWithSettingsBase<WebView2ComponentSettings>;
