using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Models.Media;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;

namespace OhMyLockScreen.Shared.Controls.MediaControls;

public sealed class MediaProgressBar : MediaPartControl
{
    private readonly Slider _slider = new() { StepFrequency = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
    private bool _updating;
    private bool _dragging;
    private bool _seeking;
    private bool _active;
    private MediaState _dragState = MediaState.Empty;
    private int _valueGeneration;
    private int _seekVersion;
    public IMediaStateService? Service { get; set; }
    public bool IsPlaceholder { get; set; }

    public MediaProgressBar()
    {
        Content = _slider;
        AutomationProperties.SetName(_slider, "媒体播放进度");
        _slider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        _slider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        _slider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnPointerCaptureLost), true);
        _slider.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnPointerCanceled), true);
        _slider.ValueChanged += OnValueChanged;
        Loaded += (_, _) =>
        {
            _active = true;
            UpdateState();
        };
        Unloaded += (_, _) =>
        {
            _active = false;
            _dragging = false;
            _seeking = false;
            ++_seekVersion;
            ++_valueGeneration;
        };
    }

    protected override void UpdateState()
    {
        _updating = true;
        try
        {
            _slider.IsEnabled = !IsPlaceholder && State.HasSession && State.CanSeek;
            if (_dragging || _seeking)
            {
                return;
            }

            _slider.Minimum = State.StartTime.TotalSeconds;
            _slider.Maximum = Math.Max(_slider.Minimum, State.EndTime.TotalSeconds);
            _slider.Value = State.GetPosition(DateTimeOffset.UtcNow).TotalSeconds;
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_slider.IsEnabled)
        {
            _dragging = true;
            _dragState = State;
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs args) => CompleteDrag();
    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs args) => CompleteDrag();
    private void OnPointerCanceled(object sender, PointerRoutedEventArgs args)
    {
        _dragging = false;
        ++_valueGeneration;
        UpdateState();
    }

    private void CompleteDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ++_valueGeneration;
        if (SameItem(_dragState, State))
        {
            CommitPosition(_slider.Value);
        }
        else
        {
            UpdateState();
        }
    }

    private void OnValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (!_updating && !_dragging && _active)
        {
            QueuePositionCommit();
        }
    }

    private async void QueuePositionCommit()
    {
        var generation = ++_valueGeneration;
        var state = State;
        var position = _slider.Value;
        // Slider may update its value before a handled PointerPressed bubbles to our handler.
        // Defer keyboard/UIA commits until the pointer gesture has had a chance to start.
        try
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                if (_active && !_dragging && generation == _valueGeneration && SameItem(state, State))
                {
                    CommitPosition(position);
                }
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法调度媒体进度更新：{exception}");
        }
    }

    private static bool SameItem(MediaState first, MediaState second) => first.SourceAppId == second.SourceAppId
        && first.Title == second.Title && first.Artist == second.Artist && first.AlbumTitle == second.AlbumTitle
        && first.TrackNumber == second.TrackNumber;

    private async void CommitPosition(double position)
    {
        if (!_active || _seeking || IsPlaceholder || Service == null || !State.CanSeek)
        {
            return;
        }

        _seeking = true;
        var version = ++_seekVersion;
        try
        {
            try
            {
                await Service.SeekAsync(TimeSpan.FromSeconds(position)).ConfigureAwait(false);
            }
            finally
            {
                await RunOnViewAsync(() =>
                {
                    if (version != _seekVersion)
                    {
                        return;
                    }

                    _seeking = false;
                    if (_active)
                    {
                        UpdateState();
                    }
                }).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"无法完成媒体进度更新：{exception}");
        }
    }
}
