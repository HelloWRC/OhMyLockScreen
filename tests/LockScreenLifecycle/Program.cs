using OhMyLockScreen;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.LockScreen;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
void Run(string name, Action test)
{
    Window.Current = new Window();
    LockApplicationHost.Acquire = () => null;
    LockScreenHostAccess.BrokerAcquire = () => null;
    CoreApplication.CurrentViewFactory = () => new();
    CoreApplication.ExitCalls = 0;
    ApplicationView.CurrentViewFactory = () => new();
    test();
    passed++;
    Console.WriteLine($"PASS: {name}");
}

Run("Hosted launch does not read launch-only interfaces", () =>
{
    var host = new LockApplicationHost();
    LockApplicationHost.Acquire = () => host;
    new App().Launch(new LaunchActivatedEventArgs());
    var page = (MainPage)((Frame)Window.Current.Content!).Content!;
    page.ClickUnlock();
    Check(host.UnlockRequests == 1, "Launch did not wire the host to Unlock.");
});

Run("Both lock activation contracts display the page", () =>
{
    var app = new App();
    foreach (var kind in new[] { ActivationKind.LockScreen, ActivationKind.LockScreenComponent })
    {
        Window.Current = new Window();
        app.Activate(new ActivatedEventArgs(kind));
        Check(((Frame)Window.Current.Content!).Content is MainPage, "Lock activation lost its view.");
    }
});

Run("Late host registration enables Unlock and stops retries", () =>
{
    var page = new MainPage();
    page.Load();
    var timer = DispatcherTimer.LastCreated!;
    Check(timer.IsEnabled && page.UnlockButton.IsEnabled, "Missing host disabled retry.");
    var host = new LockApplicationHost();
    LockApplicationHost.Acquire = () => host;
    timer.Advance();
    Check(!timer.IsEnabled && page.StatusText.Text == "", "Late host did not clear failure.");
    page.ClickUnlock();
    Check(host.UnlockRequests == 1, "Late host could not unlock.");
    page.Unload();
});

Run("Host exceptions preserve HRESULT and permit manual retry", () =>
{
    LockApplicationHost.Acquire = () => throw new UnauthorizedAccessException("Not associated yet");
    var page = new MainPage();
    page.Load();
    Check(page.StatusText.Text.Contains("0x80070005"), "HRESULT was hidden.");
    Check(!page.StatusText.Text.Contains("请在系统锁屏中使用"), "Failed query was misclassified as desktop launch.");
    var host = new LockApplicationHost();
    LockApplicationHost.Acquire = () => host;
    page.ClickUnlock();
    Check(host.UnlockRequests == 1 && page.StatusText.Text == "", "Manual retry did not recover.");
    page.Unload();
});

Run("Automatic retries are bounded and manual retry restarts them", () =>
{
    int queries = 0;
    LockApplicationHost.Acquire = () => { queries++; return null; };
    var page = new MainPage();
    page.Load();
    var timer = DispatcherTimer.LastCreated!;
    for (int i = 0; i < 20; i++) timer.Advance();
    Check(queries == 9 && !timer.IsEnabled, "Automatic retries did not stop after eight additional attempts.");
    Check(page.UnlockButton.IsEnabled, "Exhausting retries disabled Unlock.");
    page.ClickUnlock();
    Check(timer.IsEnabled && queries == 10, "Manual retry did not restart acquisition.");
    page.Unload();
});

Run("Returning from sign-in or hidden state refreshes the host", () =>
{
    var firstHost = new LockApplicationHost();
    var secondHost = new LockApplicationHost();
    LockApplicationHost.Acquire = () => firstHost;
    var page = new MainPage();
    page.Load();
    page.ClickUnlock();
    Window.Current.Deactivate();
    LockApplicationHost.Acquire = () => secondHost;
    Window.Current.Activate();
    page.ClickUnlock();
    Check(firstHost.UnlockRequests == 1 && secondHost.UnlockRequests == 1, "Sign-in return reused the old host.");
    LockApplicationHost.Acquire = () => null;
    Window.Current.SetVisible(true);
    Check(DispatcherTimer.LastCreated!.IsEnabled, "Becoming visible did not retry.");
    page.Unload();
});

Run("Failed unlock request exposes HRESULT and allows another attempt", () =>
{
    var failingHost = new LockApplicationHost { Unlock = () => throw new InvalidOperationException("Unlock failed") };
    LockApplicationHost.Acquire = () => failingHost;
    var page = new MainPage();
    page.Load();
    page.ClickUnlock();
    Check(page.StatusText.Text.Contains("0x80131509") && page.UnlockButton.IsEnabled, "Unlock failure lost its error or retry.");
    var recoveredHost = new LockApplicationHost();
    LockApplicationHost.Acquire = () => recoveredHost;
    page.ClickUnlock();
    Check(recoveredHost.UnlockRequests == 1, "Unlock request did not recover.");
    page.Unload();
});

Run("Page unload stops queries and releases window handlers", () =>
{
    int queries = 0;
    LockApplicationHost.Acquire = () => { queries++; return null; };
    var page = new MainPage();
    page.Load();
    var timer = DispatcherTimer.LastCreated!;
    page.Unload();
    int before = queries;
    Window.Current.Activate();
    Window.Current.SetVisible(true);
    timer.Advance();
    Check(queries == before && !timer.IsEnabled, "Unloaded page kept querying the host.");
    Check(Window.Current.ActivationSubscribers == 0 && Window.Current.VisibilitySubscribers == 0, "Window handlers leaked.");
});

Run("Different views acquire their own hosts", () =>
{
    var firstWindow = Window.Current;
    var secondWindow = new Window();
    var firstHost = new LockApplicationHost();
    var secondHost = new LockApplicationHost();
    LockApplicationHost.Acquire = () => Window.Current == firstWindow ? firstHost : secondHost;
    var firstPage = new MainPage();
    firstPage.Load();
    Window.Current = secondWindow;
    var secondPage = new MainPage();
    secondPage.Load();
    secondPage.ClickUnlock();
    secondPage.Unload();
    Window.Current = firstWindow;
    firstPage.ClickUnlock();
    firstPage.Unload();
    Check(firstHost.UnlockRequests == 1 && secondHost.UnlockRequests == 1, "Host leaked across views.");
});

Run("View diagnostics show the system's hosted and lock-screen flags", () =>
{
    CoreApplication.CurrentViewFactory = () => new() { IsHosted = true, IsMain = false };
    ApplicationView.CurrentViewFactory = () => new() { Id = 42, IsOnLockScreen = true };
    var page = new MainPage();
    page.Load();
    Check(page.ViewStatusText.Text.Contains("ViewId=42") && page.ViewStatusText.Text.Contains("IsHosted=True") &&
        page.ViewStatusText.Text.Contains("IsOnLockScreen=True"), "System view state was not displayed.");
    page.Unload();
});

Run("Diagnostic failure does not prevent host acquisition or Unlock", () =>
{
    CoreApplication.CurrentViewFactory = () => throw new InvalidOperationException("Diagnostics unavailable");
    var host = new LockApplicationHost();
    LockApplicationHost.Acquire = () => host;
    var page = new MainPage();
    page.Load();
    page.ClickUnlock();
    Check(host.UnlockRequests == 1 && page.ViewStatusText.Text.Contains("0x80131509"), "Diagnostics blocked the host.");
    page.Unload();
});

Run("Exit stops host tracking and exits without requesting unlock", () =>
{
    var host = new LockApplicationHost();
    var page = new MainPage();
    page.Load();
    var timer = DispatcherTimer.LastCreated!;
    Check(timer.IsEnabled, "Expected a pending retry before exit.");
    LockApplicationHost.Acquire = () => host;
    page.ClickExit();
    Check(CoreApplication.ExitCalls == 1 && host.UnlockRequests == 0, "Exit did not directly exit.");
    Check(!timer.IsEnabled && Window.Current.ActivationSubscribers == 0 && Window.Current.VisibilitySubscribers == 0,
        "Exit left host tracking active.");
});

Run("Public host success does not activate the compatibility broker", () =>
{
    var host = new LockApplicationHost();
    LockApplicationHost.Acquire = () => host;
    LockScreenHostAccess.BrokerAcquire = () => throw new Exception("Broker must not run.");
    Check(ReferenceEquals(LockScreenHostAccess.GetForCurrentView(), host), "Public host was not reused.");
});

Run("Public null can acquire a host through the compatibility broker", () =>
{
    var host = new LockApplicationHost();
    LockScreenHostAccess.BrokerAcquire = () => host;
    var page = new MainPage();
    page.Load();
    Check(page.StatusText.Text == "", "Broker host did not clear the failure.");
    page.ClickUnlock();
    Check(host.UnlockRequests == 1, "Broker host was not used for Unlock.");
    page.Unload();
});

Run("Broker refusal preserves its HRESULT and permits retry", () =>
{
    LockScreenHostAccess.BrokerAcquire = () => throw new System.Runtime.InteropServices.COMException(
        "LockFrameworkBroker.CreateLockApplicationHost failed", unchecked((int)0x80070005));
    var page = new MainPage();
    page.Load();
    Check(page.StatusText.Text.Contains("0x80070005") && page.StatusText.Text.Contains("LockFrameworkBroker"),
        "Broker failure lost its stage or HRESULT.");
    var host = new LockApplicationHost();
    LockScreenHostAccess.BrokerAcquire = () => host;
    page.ClickUnlock();
    Check(host.UnlockRequests == 1, "Retry did not reacquire the broker host.");
    page.Unload();
});

Console.WriteLine($"All {passed} lifecycle checks passed using simulated UWP views and lock hosts.");
