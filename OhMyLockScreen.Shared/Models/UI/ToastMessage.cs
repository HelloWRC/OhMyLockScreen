using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace OhMyLockScreen.Shared.Models.UI;

/// <summary>一条窗口内 Toast。通过返回的引用可提前结束显示。</summary>
public sealed class ToastMessage : ObservableObject
{
    private int _closed;
    private object? _owner;

    public ToastMessage() : this("")
    {
    }

    public ToastMessage(string message) : this("", message)
    {
    }

    public ToastMessage(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public string Title { get; init; }
    public string Message { get; init; }
    public InfoBarSeverity Severity { get; init; } = InfoBarSeverity.Informational;
    public ButtonBase? ActionContent { get; init; }
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(5);
    public bool AutoClose { get; init; } = true;
    public bool CanUserClose { get; init; } = true;
    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    internal event EventHandler? Closed;

    internal bool TryClaim(object owner) => Interlocked.CompareExchange(ref _owner, owner, null) == null;

    /// <summary>结束消息显示。可重复调用，也可用于不可由用户关闭的消息。</summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        OnPropertyChanged(nameof(IsOpen));
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
