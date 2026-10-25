using System.Collections.ObjectModel;
using OhMyLockScreen.Shared.Models.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>位于窗口右上角的非模态消息层，空白区域不拦截输入。</summary>
public sealed class AppToastAdorner : Grid
{
    private readonly StackPanel _panel = new() { Spacing = 4 };
    private readonly Canvas _interactionLayer = new();
    private readonly object _claimToken = new();
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<ToastMessage, ToastCard> _cards = [];
    private readonly ObservableCollection<ToastMessage> _messages = [];
    private double _topInset;

    public ReadOnlyObservableCollection<ToastMessage> Messages { get; }

    internal double TopInset
    {
        get => _topInset;
        set
        {
            _topInset = value;
            _scroll.Margin = new Thickness(4, value + 4, 4, 4);
        }
    }

    public AppToastAdorner()
    {
        Messages = new ReadOnlyObservableCollection<ToastMessage>(_messages);
        _scroll = new ScrollViewer
        {
            Content = _panel,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxWidth = 400,
            Margin = new Thickness(4),
            IsTabStop = false
        };
        Children.Add(_scroll);
        Children.Add(_interactionLayer);
        Unloaded += (_, _) => Clear();
    }

    internal void Show(ToastMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!message.IsOpen || _cards.ContainsKey(message))
        {
            return;
        }

        if (!message.TryClaim(_claimToken))
        {
            throw new InvalidOperationException("同一条 Toast 消息不能在不同宿主间重复显示。");
        }

        var card = new ToastCard(message, this);
        card.Removed += OnRemoved;
        _cards.Add(message, card);
        _messages.Insert(0, message);
        _panel.Children.Insert(0, card);
        _scroll.ChangeView(null, 0, null, true);
    }

    public void Clear()
    {
        if (!Dispatcher.HasThreadAccess)
        {
            throw new InvalidOperationException("请在 Toast 层所属的 UI 线程清除消息。");
        }

        foreach (var (message, card) in _cards.ToArray())
        {
            card.Removed -= OnRemoved;
            card.Dispose();
            message.Close();
        }

        _cards.Clear();
        _messages.Clear();
        _panel.Children.Clear();
    }

    internal void FloatBody(ToastCard card, Border body)
    {
        if (_interactionLayer.Children.Contains(body))
        {
            return;
        }

        // The card keeps its place in the stack while its body can travel across
        // the window without being clipped by the narrow scrolling viewport.
        var position = card.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point());
        card.Width = body.Width = card.ActualWidth;
        card.Height = body.Height = card.ActualHeight;
        card.Children.Remove(body);
        Canvas.SetLeft(body, position.X);
        Canvas.SetTop(body, position.Y);
        _interactionLayer.Children.Add(body);
    }

    internal void RestoreBody(ToastCard card, Border body)
    {
        if (!_interactionLayer.Children.Contains(body))
        {
            return;
        }

        _interactionLayer.Children.Remove(body);
        body.Width = body.Height = double.NaN;
        card.Width = card.Height = double.NaN;
        card.Children.Add(body);
    }

    private void OnRemoved(object? sender, EventArgs args)
    {
        var card = (ToastCard)sender!;
        card.Removed -= OnRemoved;
        _cards.Remove(card.Message);
        _messages.Remove(card.Message);
        _panel.Children.Remove(card);
        card.Dispose();
    }
}
