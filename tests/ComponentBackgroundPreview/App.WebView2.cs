using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Layout;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Services.Scene;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using WebView2 = Microsoft.UI.Xaml.Controls.WebView2;

namespace ComponentBackgroundPreview;

public sealed partial class App
{
    private static readonly TimeSpan WebViewTimeout = TimeSpan.FromSeconds(20);
    private const string WebPreviewAddress = "https://webview2-preview.test/";

    private void LaunchWebView2Preview(bool simulateMissingRuntime)
    {
        _status.Text = "正在验证 WebView2 网页组件…";
        if (simulateMissingRuntime)
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER",
                Path.Combine(ApplicationData.Current.LocalFolder.Path, "missing-webview2-runtime"));
        }

        IAppHost.Host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddSingleton<ILogger<WebView2Component>>(new WebPreviewLogger(_report));
            services.AddSingleton<IFeatureService, FeatureService>();
            services.AddSingleton<IComponentsService, ComponentsService>();
            services.AddFeature<LayoutFeature>();
            services.AddComponent<WebView2Component, WebView2SettingsControl>();
        }).Build();

        var model = IAppHost.GetRequiredService<ComponentRegistryService>().Find(WebView2Component.Id)!.CreateSettings();
        var settings = model.GetSettings<WebView2ComponentSettings>();
        var options = (WebView2SettingsControl)IAppHost.GetRequiredService<IComponentsService>().CreateSettingsControl(model)!;
        var underneath = new Button
        {
            Content = "网页下方按钮",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var presenter = new ComponentPresenter { Settings = model };
        var canvas = new Grid { Width = 640, Height = 360, Children = { underneath, presenter } };
        var root = new StackPanel { Spacing = 8, Children = { _status, options, canvas } };
        root.Loaded += OnLoaded;
        Window.Current.Content = root;
        Window.Current.Activate();

        async void OnLoaded(object sender, RoutedEventArgs args)
        {
            root.Loaded -= OnLoaded;
            try
            {
                await Task.Delay(100);
                var component = (WebView2Component)presenter.Content;
                Check(component.IsLoaded && component.Content == null, "An empty address leaves the realized component empty");
                if (simulateMissingRuntime)
                {
                    settings.Uri = WebPreviewAddress;
                    for (var attempt = 0; component.Content != null && attempt < 100; attempt++)
                    {
                        await Task.Delay(100);
                    }

                    Check(component.Content == null && _report.Any(line => line.StartsWith("WEBVIEW2 Error:")),
                        "Missing runtime is logged and leaves the webpage empty");
                    return;
                }

                using var html = new InMemoryRandomAccessStream();
                using var writer = new DataWriter(html);
                writer.WriteString("""
                    <!doctype html><style>body { background: #792ad4; color: white }</style>
                    <input value="WebView2 fixture"><script>document.title = location.pathname</script>
                    """);
                await writer.StoreAsync();
                writer.DetachStream();
                html.Seek(0);

                var fields = ((StackPanel)options.Content).Children;
                var input = fields.OfType<TextBox>().Single();
                var allowInteraction = fields.OfType<CheckBox>().Single();
                Check(allowInteraction.IsChecked == false, "The interaction checkbox defaults to off");
                var loaded = ObserveNextWebPage(component, html);
                Check(input.Focus(FocusState.Programmatic), "The address field can take focus");
                await Task.Delay(100);
                input.SelectAll();
                input.SelectedText = WebPreviewAddress + "first";
                Check(string.IsNullOrEmpty(settings.Uri), "Editing does not commit the address before leaving the field");
                Check(allowInteraction.Focus(FocusState.Programmatic), "The interaction checkbox can take focus");
                for (var attempt = 0; string.IsNullOrEmpty(settings.Uri) && attempt < 30; attempt++)
                {
                    await Task.Delay(100);
                }

                Check(settings.Uri == WebPreviewAddress + "first", "Losing focus commits the entered address");
                if (settings.Uri != WebPreviewAddress + "first")
                {
                    settings.Uri = WebPreviewAddress + "first";
                }
                Check(component.Content is WebView2, "The committed address creates a native WebView2 control");
                var view = await loaded;
                Check(settings.Uri == WebPreviewAddress + "first" && await view.ExecuteScriptAsync("document.title") == "\"/first\"",
                    "The committed address loads a page and runs its script");
                using var capture = new InMemoryRandomAccessStream();
                await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
                capture.Seek(0);
                var bitmap = await BitmapDecoder.CreateAsync(capture);
                Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "WebView2 renders pixels for the page");
                var point = underneath.TransformToVisual(null).TransformPoint(new Point(320, 180));
                var hits = VisualTreeHelper.FindElementsInHostCoordinates(point, root, includeAllElements: false);
                Check(hits.Contains(underneath) && !hits.Contains(presenter) && !hits.Contains(view),
                    "Hit testing reaches the button underneath the webpage");
                Check(!view.Focus(FocusState.Programmatic) && FrameworkElementAutomationPeer.CreatePeerForElement(view) == null,
                    "WebView2 cannot take focus and exposes no automation peer");

                var toggle = (IToggleProvider)FrameworkElementAutomationPeer.CreatePeerForElement(allowInteraction)
                    .GetPattern(PatternInterface.Toggle);
                presenter.IsEditing = true;
                toggle.Toggle();
                Check(settings.AllowInteraction && !presenter.IsHitTestVisible && ReferenceEquals(view, component.Content),
                    "Editing keeps the webpage passive after enabling the setting");
                loaded = ObserveNextWebPage(component, html);
                presenter.IsEditing = false;
                var interactiveView = await loaded;
                hits = VisualTreeHelper.FindElementsInHostCoordinates(point, root, includeAllElements: false);
                Check(settings.AllowInteraction && presenter.IsHitTestVisible && hits.Contains(interactiveView)
                    && interactiveView.IsTabStop && interactiveView.Focus(FocusState.Programmatic)
                    && FrameworkElementAutomationPeer.CreatePeerForElement(interactiveView) != null,
                    "Enabling interaction gives WebView2 pointer hits, keyboard focus and an automation peer");

                loaded = ObserveNextWebPage(component, html);
                toggle.Toggle();
                view = await loaded;
                hits = VisualTreeHelper.FindElementsInHostCoordinates(point, root, includeAllElements: false);
                Check(!settings.AllowInteraction && !presenter.IsHitTestVisible && hits.Contains(underneath)
                    && !hits.Contains(view) && !view.Focus(FocusState.Programmatic)
                    && FrameworkElementAutomationPeer.CreatePeerForElement(view) == null && interactiveView.CoreWebView2 == null,
                    "Disabling interaction returns input to the control underneath and closes the interactive view");

                var changed = WaitForNavigation(view);
                settings.Uri = WebPreviewAddress + "second";
                await changed.WaitAsync(WebViewTimeout);
                Check(await view.ExecuteScriptAsync("document.title") == "\"/second\"", "Changing the URI updates the live page");
                settings.Uri = "file:///invalid.html";
                Check(component.Content == null && view.CoreWebView2 == null, "Invalid URI removes and closes the previous view");

                settings.Uri = WebPreviewAddress + "discard";
                var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                component.Unloaded += (_, _) => unloaded.TrySetResult();
                canvas.Children.Remove(presenter);
                await unloaded.Task.WaitAsync(WebViewTimeout);
                settings.Uri = "";
                canvas.Children.Add(presenter);
                await Task.Delay(100);
                component = (WebView2Component)presenter.Content;
                Check(component.Content == null, "Reload after clearing the URI has no stale page");
                loaded = ObserveNextWebPage(component, html);
                settings.Uri = WebPreviewAddress + "reloaded";
                view = await loaded;
                Check(await view.ExecuteScriptAsync("document.title") == "\"/reloaded\"", "WebView2 initializes again after reload");
            }
            catch (Exception exception)
            {
                _report.Add("FAIL: " + exception);
            }

            finally
            {
                _status.Text = _report.Any(line => line.StartsWith("FAIL:")) ? "FAIL" : "PASS: WebView2";
                var reportName = simulateMissingRuntime ? "webview2-missing-runtime-report.txt" : "webview2-report.txt";
                File.WriteAllLines(Path.Combine(ApplicationData.Current.LocalFolder.Path, reportName), _report);
            }
        }
    }

    private sealed class WebPreviewLogger(List<string> report) : ILogger<WebView2Component>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            report.Add($"WEBVIEW2 {logLevel}: {formatter(state, exception)} {exception}");
        }
    }

    private static async Task<WebView2> ObserveNextWebPage(WebView2Component component, IRandomAccessStream html)
    {
        var completed = new TaskCompletionSource<WebView2>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = component.RegisterPropertyChangedCallback(UserControl.ContentProperty, (_, _) =>
        {
            if (component.Content is not WebView2 view)
            {
                return;
            }

            view.CoreWebView2Initialized += (_, args) =>
            {
                if (view.CoreWebView2 is not { } core)
                {
                    completed.TrySetException(args.Exception ?? new InvalidOperationException("WebView2 Runtime unavailable"));
                    return;
                }

                core.AddWebResourceRequestedFilter(WebPreviewAddress + "*", CoreWebView2WebResourceContext.Document);
                core.WebResourceRequested += (_, request) => request.Response = core.Environment.CreateWebResourceResponse(
                    html.CloneStream(), 200, "OK", "Content-Type: text/html; charset=utf-8");
            };
            view.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    completed.TrySetResult(view);
                }
                else
                {
                    completed.TrySetException(new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}"));
                }
            };
        });

        try
        {
            return await completed.Task.WaitAsync(WebViewTimeout);
        }
        finally
        {
            component.UnregisterPropertyChangedCallback(UserControl.ContentProperty, token);
        }
    }

    private static Task WaitForNavigation(WebView2 view)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.NavigationCompleted += Complete;
        return completion.Task;

        void Complete(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            sender.NavigationCompleted -= Complete;
            if (args.IsSuccess)
            {
                completion.TrySetResult();
            }
            else
            {
                completion.TrySetException(new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}"));
            }
        }
    }
}
