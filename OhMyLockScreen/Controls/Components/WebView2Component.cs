using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Attributes.Scene;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;

namespace OhMyLockScreen.Controls.Components;

[ComponentInfo(Id, "网页（WebView2）", FluentIcons.Common.Icon.Globe, "显示指定地址的网页。")]
[ComponentDefaultFeatures(typeof(WebView2ComponentDefaults))]
public sealed class WebView2Component : ComponentBase<WebView2ComponentSettings>
{
    public const string Id = "omls.components.webView2";

    private readonly ILogger<WebView2Component> _logger;
    private readonly CoreDispatcher _dispatcher;
    private readonly Window _window;
    private WebView2ComponentSettings? _observedSettings;
    private WebView2? _webView;
    private string? _requestedAddress;
    private bool _allowsInteraction;
    private bool _initializing;
    private bool _loaded;

    public WebView2Component(ILogger<WebView2Component> logger)
    {
        _logger = logger;
        _dispatcher = Dispatcher;
        _window = Window.Current;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsHitTestVisible = false;
        IsEnabled = false;
        IsTabStop = false;
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            if (!IsLoaded)
            {
                Deactivate();
            }
        };
    }

    protected override void OnSettingsChanged()
    {
        if (_loaded)
        {
            ObserveSettings(Settings);
            ApplySettings();
        }
    }

    protected override void OnIsEditingChanged()
    {
        if (_loaded && _observedSettings?.AllowInteraction == true)
        {
            ApplySettings();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _window.Closed += OnWindowClosed;
        OnSettingsChanged();
    }

    private void OnWindowClosed(object sender, CoreWindowEventArgs args) => Deactivate();

    private void Deactivate()
    {
        if (!_loaded)
        {
            return;
        }

        _loaded = false;
        _window.Closed -= OnWindowClosed;
        ObserveSettings(null);
        RemoveWebView();
    }

    private void ObserveSettings(WebView2ComponentSettings? settings)
    {
        if (_observedSettings != null)
        {
            _observedSettings.PropertyChanged -= OnComponentSettingsChanged;
        }

        _observedSettings = settings;
        if (settings != null)
        {
            settings.PropertyChanged += OnComponentSettingsChanged;
        }
    }

    private async void OnComponentSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(Settings.Uri) or nameof(Settings.AllowInteraction) or null or ""))
        {
            return;
        }

        try
        {
            if (_dispatcher.HasThreadAccess)
            {
                ApplyCurrentSettings();
            }
            else
            {
                await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, ApplyCurrentSettings);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "无法更新网页组件设置");
        }

        void ApplyCurrentSettings()
        {
            if (_loaded && ReferenceEquals(sender, _observedSettings))
            {
                ApplySettings();
            }
        }
    }

    private void ApplySettings()
    {
        var allowsInteraction = !IsEditing && _observedSettings?.AllowInteraction == true;
        if (allowsInteraction != _allowsInteraction)
        {
            // A WebView2 automation peer is cached for the lifetime of its control.
            RemoveWebView();
            _allowsInteraction = allowsInteraction;
            IsEnabled = allowsInteraction;
            IsHitTestVisible = allowsInteraction;
        }

        ApplyAddress();
    }

    private void ApplyAddress()
    {
        var address = WebView2ComponentSettings.ParseAddress(_observedSettings?.Uri);
        if (address == null)
        {
            RemoveWebView();
            return;
        }

        if (_webView == null)
        {
            try
            {
                var view = _allowsInteraction ? new WebView2() : new DisplayWebView();
                Content = view;
                _webView = view;
                _initializing = true;
                _ = InitializeAsync(view);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "无法创建网页组件");
            }
        }
        else if (!_initializing && _requestedAddress != address.AbsoluteUri)
        {
            Navigate(_webView, address);
        }
    }

    private async Task InitializeAsync(WebView2 view)
    {
        Exception? initializationError = null;
        try
        {
            await view.EnsureCoreWebView2Async();
        }
        catch (Exception exception)
        {
            initializationError = exception;
        }

        try
        {
            if (_dispatcher.HasThreadAccess)
            {
                CompleteInitialization(view, initializationError);
            }
            else
            {
                await _dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () => CompleteInitialization(view, initializationError));
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "无法完成网页组件初始化");
        }
    }

    private void CompleteInitialization(WebView2 view, Exception? initializationError)
    {
        if (!ReferenceEquals(view, _webView))
        {
            CloseWebView(view);
            return;
        }

        _initializing = false;
        if (initializationError != null || view.CoreWebView2 is not { } core)
        {
            _logger.LogError(initializationError, "无法初始化网页组件，请检查 WebView2 运行时");
            RemoveWebView();
            return;
        }

        if (WebView2ComponentSettings.ParseAddress(_observedSettings?.Uri) is not { } address)
        {
            RemoveWebView();
            return;
        }

        try
        {
            ConfigureBrowser(core);
            view.NavigationCompleted += OnNavigationCompleted;
            view.CoreProcessFailed += OnCoreProcessFailed;
            Navigate(view, address);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "无法配置网页组件");
            RemoveWebView();
        }
    }

    private static void ConfigureBrowser(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;

        core.NavigationStarting += (_, args) =>
            args.Cancel = WebView2ComponentSettings.ParseAddress(args.Uri) == null;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.BasicAuthenticationRequested += (_, args) => args.Cancel = true;
        core.ClientCertificateRequested += (_, args) =>
        {
            args.Cancel = true;
            args.Handled = true;
        };
        core.DownloadStarting += (_, args) =>
        {
            args.Handled = true;
            args.Cancel = true;
        };
    }

    private void Navigate(WebView2 view, Uri address)
    {
        try
        {
            _requestedAddress = address.AbsoluteUri;
            view.CoreWebView2.Navigate(address.AbsoluteUri);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "无法导航到网页组件的配置地址");
            RemoveWebView();
        }
    }

    private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess && args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
        {
            _logger.LogWarning("网页组件加载失败：{WebErrorStatus}", args.WebErrorStatus);
        }
    }

    private void OnCoreProcessFailed(WebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        _logger.LogWarning("网页组件进程失败：{ProcessFailedKind}", args.ProcessFailedKind);
        if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            RemoveWebView();
        }
    }

    private void RemoveWebView()
    {
        var view = _webView;
        var initializing = _initializing;
        _webView = null;
        _requestedAddress = null;
        _initializing = false;
        Content = null;
        // WinUI can still create a controller after Close if initialization is pending.
        if (view != null && !initializing)
        {
            CloseWebView(view);
        }
    }

    private void CloseWebView(WebView2 view)
    {
        view.NavigationCompleted -= OnNavigationCompleted;
        view.CoreProcessFailed -= OnCoreProcessFailed;
        try
        {
            view.Close();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "无法关闭网页组件");
        }
    }

    private sealed class DisplayWebView : WebView2
    {
        public DisplayWebView()
        {
            IsHitTestVisible = false;
            IsEnabled = false;
            IsTabStop = false;
            AllowDrop = false;
            GettingFocus += (_, args) => args.TryCancel();
        }

        protected override AutomationPeer OnCreateAutomationPeer() => null!;
    }
}
