using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Abstractions.Models.Scene;
using OhMyLockScreen.Shared.Attributes.Serialization;
using Windows.UI.Xaml.Data;

namespace OhMyLockScreen.Models.Scene;

[AutoJsonTypeName("omls.webView2")]
[Bindable]
public partial class WebView2ComponentSettings : ObservableObject, IComponentSettings
{
    [ObservableProperty]
    private string _uri = "";

    [ObservableProperty]
    private bool _allowInteraction;

    internal static System.Uri? ParseAddress(string? address)
    {
        return System.Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri)
            && !string.IsNullOrEmpty(uri.Host)
            && (uri.Scheme == System.Uri.UriSchemeHttp || uri.Scheme == System.Uri.UriSchemeHttps)
            ? uri
            : null;
    }
}
