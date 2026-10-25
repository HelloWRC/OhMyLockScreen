using Microsoft.UI.Xaml.Controls;
using OhMyLockScreen.Helpers.UI;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Helpers.UI;
using OhMyLockScreen.Shared.Models.UI;
using Windows.ApplicationModel.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

internal static class ToastTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Toast helpers preserve reference, severity, duration and action button contracts", () =>
        {
            var host = CreateHost();
            var source = new FrameworkElement { VisualParent = host.Frame };
            var action = new ButtonBase();
            var message = new ToastMessage("标题", "内容") { ActionContent = action };
            Check(ReferenceEquals(source.ShowToastRef(message), message), "Ref overload replaced the message.");
            Check(ReferenceEquals(message.ActionContent, action) && message.IsOpen && message.AutoClose && message.CanUserClose,
                "The default toast or action contract changed.");
            Check(message.Duration == TimeSpan.FromSeconds(5), "Default duration changed.");
            Check(source.ShowWarningToastRef("警告").Severity == InfoBarSeverity.Warning, "Warning severity was lost.");
            Check(source.ShowSuccessToastRef("完成").Severity == InfoBarSeverity.Success, "Success severity was lost.");
            var error = Window.Current.ShowErrorToastRef("失败");
            Check(error.Severity == InfoBarSeverity.Error && error.Duration == TimeSpan.FromSeconds(10), "Error duration changed.");
            var exception = source.ShowErrorToastRef("保存失败", new IOException("磁盘不可写"));
            Check(!exception.AutoClose && exception.Title == "保存失败" && exception.Message == "磁盘不可写", "Exception details or persistence were lost.");
            Window.Current.ShowToast("普通消息");
            Check(host.Toasts.Messages[0].Message == "普通消息" && host.Toasts.Messages[^1] == message, "New messages were not inserted at the top.");
            host.Clear();
        });

        run("Toast routing uses the source view, supports popup roots and rejects detached controls", () =>
        {
            var firstWindow = Window.Current;
            var first = CreateHost();
            var source = new FrameworkElement { VisualParent = first.Frame };
            var popup = new FrameworkElement { XamlRoot = new XamlRoot { Content = first } };
            popup.ShowToast("弹出层消息");
            Window.Current = new Window();
            var second = CreateHost();
            Window.Current.ShowToast("第二个窗口");
            Throws<InvalidOperationException>(() => source.ShowToast("跨视图误投递"));
            Throws<InvalidOperationException>(() => new FrameworkElement().ShowToast("未挂载"));
            Check(first.Toasts.Messages.Count == 1 && second.Toasts.Messages.Count == 1, "Messages crossed window boundaries.");
            Window.Current.Close();
            Check(second.Toasts.Messages.Count == 0 && first.Toasts.Messages.Count == 1, "Closing a window cleared another view.");
            Window.Current = firstWindow;
            firstWindow.Close();
        });

        run("Closed and duplicate toast references cannot create duplicate entries or cross-view controls", () =>
        {
            var window = Window.Current;
            var host = CreateHost();
            var message = window.ShowToastRef("一条消息");
            window.ShowToast(message);
            Check(host.Toasts.Messages.Count == 1, "The same message was inserted twice.");
            var closed = new ToastMessage("已关闭");
            closed.Close();
            window.ShowToast(closed);
            Check(host.Toasts.Messages.Count == 1, "A closed message was displayed.");
            Window.Current = new Window();
            var other = CreateHost();
            Throws<InvalidOperationException>(() => Window.Current.ShowToast(message));
            Check(other.Toasts.Messages.Count == 0 && message.IsOpen, "Sharing a message changed its original owner.");
            Window.Current.Close();
            Window.Current = window;
            host.Clear();
        });

        run("Toast host unload and window close release entries and subscriptions and permit reattachment", () =>
        {
            var host = CreateHost();
            var first = Window.Current.ShowToastRef("待清理");
            var panel = (StackPanel)((ScrollViewer)host.Toasts.Children[0]).Content!;
            var card = (ToastCard)panel.Children[0];
            host.Unload();
            Check(!first.IsOpen && card.Disposed && host.Toasts.Messages.Count == 0 && Window.Current.ClosedSubscribers == 0,
                "Unload retained a message, card or window event.");
            Throws<InvalidOperationException>(() => host.Show(new ToastMessage("过期宿主")));
            host.Load();
            host.Load();
            Check(Window.Current.ClosedSubscribers == 1, "Reload duplicated window subscriptions.");
            var second = Window.Current.ShowToastRef("重新连接");
            Window.Current.Close();
            Check(!second.IsOpen && host.Toasts.Messages.Count == 0 && Window.Current.ClosedSubscribers == 0, "Window close leaked toast state.");
        });

        run("Toast overlay follows page theme and reserves the extended title bar only when visible", () =>
        {
            var host = CreateHost();
            host.Frame.Navigate(typeof(Page));
            var page = (Page)host.Frame.Content!;
            page.ChangeTheme(ElementTheme.Dark);
            Check(host.Toasts.RequestedTheme == ElementTheme.Dark, "The overlay did not follow the page theme.");
            var title = CoreApplication.GetCurrentView().TitleBar;
            title.ExtendViewIntoTitleBar = true;
            title.Height = 40;
            host.RaiseLayoutUpdated();
            var scroll = (ScrollViewer)host.Toasts.Children[0];
            Check(scroll.Margin.Top == 44 && scroll.VerticalAlignment == VerticalAlignment.Top && scroll.HorizontalAlignment == HorizontalAlignment.Right,
                "Toasts were not at the top right below the title bar.");
            title.IsVisible = false;
            host.RaiseLayoutUpdated();
            Check(scroll.Margin.Top == 4, "Fullscreen retained an invisible title-bar inset.");
            host.Unload();
        });

        run("Toast automatic close preserves remaining time through drag and rebound pauses", () =>
        {
            var time = new ManualTimeProvider();
            var message = new ToastMessage("拖动中的消息");
            using var lifetime = new ToastLifetime(message, time, action => action());
            lifetime.Resume();
            time.Advance(TimeSpan.FromSeconds(2));
            lifetime.Pause();
            time.Advance(TimeSpan.FromMinutes(1));
            Check(message.IsOpen && lifetime.Remaining == TimeSpan.FromSeconds(3), "Interaction consumed the remaining duration.");
            lifetime.Resume();
            lifetime.Resume();
            time.Advance(TimeSpan.FromSeconds(2));
            Check(message.IsOpen, "Repeated resume reset or shortened the deadline.");
            time.Advance(TimeSpan.FromSeconds(1));
            Check(!message.IsOpen, "Toast did not close after its remaining duration.");
        });

        run("Queued timeout callbacks cannot close a paused or disposed toast", () =>
        {
            var time = new ManualTimeProvider();
            var pending = new Queue<Action>();
            var message = new ToastMessage("暂停");
            using var lifetime = new ToastLifetime(message, time, pending.Enqueue);
            lifetime.Resume();
            time.Advance(TimeSpan.FromSeconds(5));
            lifetime.Pause();
            pending.Dequeue()();
            Check(message.IsOpen, "A queued timer closed a paused toast.");
            lifetime.Resume();
            time.Advance(TimeSpan.Zero);
            lifetime.Dispose();
            pending.Dequeue()();
            Check(message.IsOpen && time.ActiveTimers == 0, "A disposed lifetime retained a timer or callback.");
        });

        run("Persistent toasts ignore duration while non-user-closable toasts still auto-close", () =>
        {
            var time = new ManualTimeProvider();
            var persistent = new ToastMessage("持续提示") { AutoClose = false };
            using var first = new ToastLifetime(persistent, time, action => action());
            first.Resume();
            time.Advance(TimeSpan.FromDays(1));
            Check(persistent.IsOpen, "A persistent message expired.");
            var automatic = new ToastMessage("后台操作") { CanUserClose = false };
            using var second = new ToastLifetime(automatic, time, action => action());
            second.Resume();
            time.Advance(TimeSpan.FromSeconds(5));
            Check(!automatic.IsOpen, "User-close policy incorrectly disabled automatic close.");
            var immediate = new ToastMessage("立即结束") { Duration = TimeSpan.FromSeconds(-1) };
            using var third = new ToastLifetime(immediate, time, action => action());
            third.Resume();
            time.Advance(TimeSpan.Zero);
            Check(!immediate.IsOpen, "Nonpositive duration did not close safely.");
        });

        run("Toast Close is idempotent and still works for non-user-closable messages", () =>
        {
            var message = new ToastMessage("任务") { CanUserClose = false };
            var notifications = 0;
            var closes = 0;
            message.PropertyChanged += (_, args) => notifications += args.PropertyName == nameof(ToastMessage.IsOpen) ? 1 : 0;
            message.Closed += (_, _) => closes++;
            Parallel.For(0, 20, _ => message.Close());
            Check(!message.IsOpen && notifications == 1 && closes == 1, "Repeated close notified more than once.");
        });

        run("Toast swipe distinguishes vertical scrolling, deliberate horizontal drags and quick flings", () =>
        {
            Check(!ToastSwipeGesture.IsHorizontal(7, 2) && ToastSwipeGesture.IsHorizontal(12, 3), "Horizontal intent threshold changed.");
            Check(ToastSwipeGesture.IsVertical(12, 20) && !ToastSwipeGesture.IsHorizontal(12, 20), "Vertical scrolling was consumed.");
            Check(!ToastSwipeGesture.ShouldDismiss(80, 0, 400, true) && ToastSwipeGesture.ShouldDismiss(97, 0, 400, true), "Distance dismissal failed.");
            Check(ToastSwipeGesture.ShouldDismiss(-20, -900, 400, true) && ToastSwipeGesture.ShouldDismiss(20, 900, 400, true), "Bidirectional flings failed.");
            Check(!ToastSwipeGesture.ShouldDismiss(20, -900, 400, true) && !ToastSwipeGesture.ShouldDismiss(10, 1200, 400, true), "Opposite or tiny flings incorrectly dismissed.");
            Check(ToastSwipeGesture.ShouldDismiss(49, 0, 120, true), "Narrow-window threshold did not stay usable.");
        });

        run("Non-user-closable toasts resist dragging and can be re-grabbed during their rebound", () =>
        {
            var offset = ToastSwipeGesture.Resist(-300, 320);
            Check(Math.Abs(offset) < ToastSwipeGesture.Threshold(320), "The drag escaped the bounded resistance.");
            Check(Math.Abs(ToastSwipeGesture.Unresist(offset, 320) + 300) < 0.001, "Re-grabbing a rebound would jump.");
            Check(!ToastSwipeGesture.ShouldDismiss(offset, -3000, 320, false), "A non-user-closable toast was dismissed.");
        });

        run("Saving recovery closes only that scene's latest failure toast", () =>
        {
            var tracker = new SceneSaveToastTracker();
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            var previous = new ToastMessage("旧失败");
            var latest = new ToastMessage("新失败");
            var other = new ToastMessage("另一场景失败");
            var unrelated = new ToastMessage("背景播放失败");
            tracker.Replace(firstId, previous);
            tracker.Replace(secondId, other);
            tracker.Replace(firstId, latest);
            Check(!previous.IsOpen && latest.IsOpen && other.IsOpen, "Replacing one failure affected another scene.");
            tracker.Close(firstId);
            tracker.Close(firstId);
            Check(!latest.IsOpen && other.IsOpen && unrelated.IsOpen, "Saving recovery closed an unrelated message.");
            other.Close();
            tracker.Clear();
            Check(unrelated.IsOpen, "Clearing tracked failures affected unrelated hints.");
        });
    }

    private static WindowToastHost CreateHost()
    {
        var host = new WindowToastHost(Window.Current, new Frame());
        Window.Current.Content = host;
        return host;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        private readonly List<ManualTimer> _timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        internal int ActiveTimers => _timers.Count;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        internal void Advance(TimeSpan elapsed)
        {
            _timestamp += elapsed.Ticks;
            foreach (var timer in _timers.Where(timer => timer.Due <= _timestamp).ToArray())
            {
                timer.Fire();
            }
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            internal long Due { get; private set; } = long.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner._timestamp + dueTime.Ticks;
                return true;
            }

            internal void Fire()
            {
                Due = long.MaxValue;
                callback(state);
            }

            public void Dispose() => owner._timers.Remove(this);
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
