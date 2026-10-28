using OhMyLockScreen.Shared.Models.Media;
using System.Runtime.ExceptionServices;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.MediaControls;

/// <summary>接收不可变媒体快照的基础呈现控件。</summary>
public abstract class MediaPartControl : UserControl
{
    private readonly CoreDispatcher _viewDispatcher;

    protected MediaPartControl() => _viewDispatcher = Dispatcher;

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(MediaState), typeof(MediaPartControl),
        new PropertyMetadata(null, (sender, _) => ((MediaPartControl)sender).UpdateState()));

    public MediaState State
    {
        get => (MediaState?)GetValue(StateProperty) ?? MediaState.Empty;
        set => SetValue(StateProperty, value);
    }

    protected abstract void UpdateState();

    /// <summary>异步工作完成后显式返回所属视图；关闭中的 Dispatcher 可以跳过更新。</summary>
    protected async Task RunOnViewAsync(Action update)
    {
        if (_viewDispatcher.HasThreadAccess)
        {
            update();
            return;
        }

        Exception? failure = null;
        await _viewDispatcher.TryRunAsync(CoreDispatcherPriority.Normal, () =>
        {
            try
            {
                update();
            }
            catch (Exception exception)
            {
                // Do not let an exception escape the native dispatcher callback.
                failure = exception;
            }
        });

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
