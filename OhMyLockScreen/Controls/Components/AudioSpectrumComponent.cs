using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.Audio;
using OhMyLockScreen.Shared.Abstractions.Controls.Scene;
using OhMyLockScreen.Shared.Abstractions.Services.Audio;
using OhMyLockScreen.Shared.Abstractions.Services.Media;
using OhMyLockScreen.Shared.Attributes.Scene;
using OhMyLockScreen.Shared.Models.Audio;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace OhMyLockScreen.Controls.Components;

[ComponentInfo(Id, "音频频谱", FluentIcons.Common.Icon.MusicNote1, "使用系统输出音频显示柱状频谱。")]
public sealed class AudioSpectrumComponent : ComponentBase<AudioSpectrumComponentSettings>
{
    public const string Id = "omls.components.audioSpectrum";
    private readonly IAudioSpectrumService _spectrum;
    private readonly IMediaStateService _media;
    private readonly CoreDispatcher _viewDispatcher;
    private readonly Window _viewWindow;
    private readonly Rectangle _paint = new() { IsHitTestVisible = false };
    private readonly SpectrumPresentation _presentation = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1d / 30) };
    private readonly Stopwatch _clock = new();
    private AudioSpectrumComponentSettings? _observed;
    private IDisposable? _lease;
    private CompositionPathGeometry? _geometry;
    private CompositionGeometricClip? _clip;
    private CanvasDevice? _device;
    private double[] _placeholder = [];
    private TimeSpan _lastFrame;
    private int _generation;
    private int _queued;
    private bool _loaded;

    public AudioSpectrumComponent(IAudioSpectrumService spectrum, IMediaStateService media)
    {
        _spectrum = spectrum;
        _media = media;
        _viewDispatcher = Dispatcher;
        _viewWindow = Window.Current;
        Content = _paint;
        IsTabStop = false;
        AutomationProperties.SetName(this, "音频频谱");
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => UpdateBrush());
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => Draw();
        _timer.Tick += (_, _) => Draw();
    }

    protected override void OnSettingsChanged()
    {
        ObserveSettings();
        Refresh();
    }

    protected override void OnIsEditingChanged() => Draw();

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        AudioSpectrumTrace.Write($"View loaded; hosted={Windows.ApplicationModel.Core.CoreApplication.GetCurrentView().IsHosted}; context={System.Threading.SynchronizationContext.Current?.GetType().Name ?? "null"}");
        _viewWindow.Closed += OnWindowClosed;
        ++_generation;
        _clock.Restart();
        _lastFrame = TimeSpan.Zero;
        var visual = ElementCompositionPreview.GetElementVisual(_paint);
        _geometry = visual.Compositor.CreatePathGeometry();
        _clip = visual.Compositor.CreateGeometricClip(_geometry);
        visual.Clip = _clip;
        _device = new CanvasDevice();
        _lease = _spectrum.Acquire();
        _media.PropertyChanged += OnChanged;
        ObserveSettings();
        Refresh();
        _timer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
        => Deactivate();

    private void OnWindowClosed(object sender, CoreWindowEventArgs args)
        => Deactivate();

    private void Deactivate()
    {
        if (!_loaded)
        {
            return;
        }

        _loaded = false;
        _viewWindow.Closed -= OnWindowClosed;
        ++_generation;
        _timer.Stop();
        _clock.Stop();
        _presentation.Reset();
        _media.PropertyChanged -= OnChanged;
        ObserveSettings();
        _lease?.Dispose();
        _lease = null;
        ElementCompositionPreview.GetElementVisual(_paint).Clip = null;
        _clip?.Dispose();
        _clip = null;
        _geometry?.Dispose();
        _geometry = null;
        _device?.Dispose();
        _device = null;
        _paint.Fill = null;
    }

    private void ObserveSettings()
    {
        if (_observed != null)
        {
            _observed.PropertyChanged -= OnChanged;
        }

        _observed = _loaded ? SettingsInternal as AudioSpectrumComponentSettings : null;
        if (_observed != null)
        {
            _observed.PropertyChanged += OnChanged;
        }
    }

    private async void OnChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (Interlocked.Exchange(ref _queued, 1) != 0)
        {
            return;
        }

        var generation = _generation;
        try
        {
            await _viewDispatcher.TryRunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Interlocked.Exchange(ref _queued, 0);
                if (_loaded && generation == _generation)
                {
                    Refresh();
                }
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
        }
        finally
        {
            Interlocked.Exchange(ref _queued, 0);
        }
    }

    private void Refresh()
    {
        if (!_loaded || SettingsInternal is not AudioSpectrumComponentSettings)
        {
            return;
        }

        _placeholder = SpectrumPresentation.Placeholder(Settings.BarCount);
        InvalidateMeasure();
        UpdateBrush();
        Draw();
    }

    private void UpdateBrush()
    {
        if (!_loaded || SettingsInternal is not AudioSpectrumComponentSettings)
        {
            return;
        }

        var state = _media.State;
        if (Settings.ColorSource == SpectrumColorSource.MediaCover && state.HasSession && !state.Cover.IsEmpty)
        {
            var argb = state.ThemeColor;
            var color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            if (_paint.Fill is not SolidColorBrush solid || solid.Color != color || ReferenceEquals(solid, Foreground))
            {
                _paint.Fill = new SolidColorBrush(color);
            }
        }
        else
        {
            _paint.Fill = Foreground;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var vertical = SettingsInternal is AudioSpectrumComponentSettings { Dock: SpectrumDock.Left or SpectrumDock.Right };
        var desired = new Size(Math.Min(availableSize.Width, vertical ? 120 : 320),
            Math.Min(availableSize.Height, vertical ? 320 : 120));
        base.MeasureOverride(desired);
        return desired;
    }

    private void Draw()
    {
        if (!_loaded || _device == null || _geometry == null || SettingsInternal is not AudioSpectrumComponentSettings)
        {
            return;
        }

        var now = _clock.Elapsed;
        UpdateBrush();
        // Sampling the latest immutable snapshot on the view's timer coalesces all capture updates.
        _presentation.Update(_spectrum.State, Settings.BarCount, Settings.Sensitivity, now - _lastFrame, DateTimeOffset.UtcNow);
        _lastFrame = now;
        var levels = IsEditing && !_presentation.HasSignal ? _placeholder.AsSpan() : _presentation.Levels.Span;
        var bars = SpectrumPresentation.Arrange(levels, ActualWidth, ActualHeight, Settings.Dock, Settings.Gap);
        using var builder = new CanvasPathBuilder(_device);
        foreach (var bar in bars)
        {
            if (bar.Width <= 0 || bar.Height <= 0)
            {
                continue;
            }

            builder.BeginFigure((float)bar.X, (float)bar.Y);
            builder.AddLine((float)(bar.X + bar.Width), (float)bar.Y);
            builder.AddLine((float)(bar.X + bar.Width), (float)(bar.Y + bar.Height));
            builder.AddLine((float)bar.X, (float)(bar.Y + bar.Height));
            builder.EndFigure(CanvasFigureLoop.Closed);
        }

        using var geometry = CanvasGeometry.CreatePath(builder);
        _geometry.Path = new CompositionPath(geometry);
    }
}
