using Windows.UI.Xaml.Controls;

using System;
using System.Diagnostics;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.LockScreen;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.ViewManagement;

namespace OhMyLockScreen
{
    /// <summary>
    /// Minimal lock screen content with a system unlock request.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private LockApplicationHost? lockHost;
        private readonly DispatcherTimer hostRetryTimer;
        private Window? viewWindow;
        private int retriesRemaining;
        private bool isAcquiringHost;
        private bool isPageLoaded;

        public MainPage()
        {
            InitializeComponent();
            hostRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            hostRetryTimer.Tick += OnHostRetry;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        internal void InitializeUnlock()
        {
            if (isAcquiringHost)
            {
                return;
            }

            hostRetryTimer.Stop();
            lockHost = null;
            retriesRemaining = 8;

            // The hosted CoreWindow can be visible before its lock-host association
            // is ready. All attempts stay on this view's UI thread, including retries.
            if (!TryAcquireHost() && isPageLoaded)
            {
                hostRetryTimer.Start();
            }
        }

        private bool TryAcquireHost()
        {
            if (isAcquiringHost)
            {
                return false;
            }

            isAcquiringHost = true;
            // A failed query must not permanently disable the only way to retry.
            UnlockButton.IsEnabled = true;
            try
            {
                UpdateViewStatus();
                lockHost = LockScreenHostAccess.GetForCurrentView();
                StatusText.Text = lockHost != null
                    ? string.Empty
                    : "当前视图尚未获得锁屏宿主，请点击 Unlock 重试。";
                return lockHost != null;
            }
            catch (Exception exception)
            {
                lockHost = null;
                StatusText.Text = $"无法获取锁屏宿主 (0x{exception.HResult:X8})：{exception.Message} 请点击 Unlock 重试。";
                Debug.WriteLine($"LockScreenHostAccess.GetForCurrentView: {exception}");
                return false;
            }
            finally
            {
                isAcquiringHost = false;
            }
        }

        private void UpdateViewStatus()
        {
            try
            {
                var coreView = CoreApplication.GetCurrentView();
                var appView = ApplicationView.GetForCurrentView();
                ViewStatusText.Text = $"ViewId={appView.Id}, IsHosted={coreView.IsHosted}, IsMain={coreView.IsMain}, IsOnLockScreen={appView.IsOnLockScreen}";
            }
            catch (Exception exception)
            {
                // Diagnostic failure must not prevent acquiring the actual host.
                ViewStatusText.Text = $"读取当前视图状态失败 (0x{exception.HResult:X8})";
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            isPageLoaded = true;
            viewWindow = Window.Current;
            viewWindow.Activated += OnWindowActivated;
            viewWindow.VisibilityChanged += OnWindowVisibilityChanged;
            InitializeUnlock();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            isPageLoaded = false;
            hostRetryTimer.Stop();
            lockHost = null;
            if (viewWindow != null)
            {
                viewWindow.Activated -= OnWindowActivated;
                viewWindow.VisibilityChanged -= OnWindowVisibilityChanged;
                viewWindow = null;
            }
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState != CoreWindowActivationState.Deactivated)
            {
                InitializeUnlock();
            }
        }

        private void OnWindowVisibilityChanged(object sender, VisibilityChangedEventArgs e)
        {
            if (e.Visible)
            {
                InitializeUnlock();
            }
        }

        private void OnHostRetry(object? sender, object e)
        {
            if (TryAcquireHost() || --retriesRemaining <= 0)
            {
                hostRetryTimer.Stop();
            }
        }

        private void ButtonUnlock_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            // Reacquire for this view, including after a cancelled sign-in attempt.
            InitializeUnlock();
            if (lockHost == null)
            {
                return;
            }

            try
            {
                hostRetryTimer.Stop();
                lockHost.RequestUnlock();
            }
            catch (Exception exception)
            {
                StatusText.Text = $"解锁请求失败 (0x{exception.HResult:X8})，请重试。";
                Debug.WriteLine($"LockApplicationHost.RequestUnlock: {exception}");
            }
        }

        private void ButtonExit_Click(object sender, RoutedEventArgs e)
        {
            OnUnloaded(sender, e);
            CoreApplication.Exit();
        }
    }
}
