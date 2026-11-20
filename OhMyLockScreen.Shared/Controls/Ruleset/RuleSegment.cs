// Selection transition adapted from ClassIsland.Core AnimatedIconButton (GPL-3.0).

using OhMyLockScreen.Shared.Models.Ruleset;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace OhMyLockScreen.Shared.Controls.Ruleset;

public sealed partial class RuleSegment : UserControl
{
    private readonly UISettings _uiSettings = new();
    private readonly LabelMotion[] _motions;
    private bool _ready;
    private int _generation;

    public string FirstLabel { get; set; } = "任一满足时";
    public string SecondLabel { get; set; } = "全部满足时";
    public string FirstGlyph { get; set; } = "\uE8FD";
    public string SecondGlyph { get; set; } = "\uE8B3";

    public static readonly DependencyProperty SecondSelectedProperty = DependencyProperty.Register(nameof(SecondSelected),
        typeof(bool), typeof(RuleSegment), new PropertyMetadata(false, (s, _) => ((RuleSegment)s).SyncSelection()));
    public bool SecondSelected
    {
        get => (bool)GetValue(SecondSelectedProperty);
        set => SetValue(SecondSelectedProperty, value);
    }

    public RuleSegment()
    {
        InitializeComponent();
        _motions =
        [
            new(FirstViewport, FirstText, FirstTransition, FirstWidthAnimation, FirstOpacityAnimation),
            new(SecondViewport, SecondText, SecondTransition, SecondWidthAnimation, SecondOpacityAnimation)
        ];
        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _ready = false;
            ++_generation;
            foreach (var motion in _motions) motion.Reset();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        var generation = ++_generation;
        FirstText.Text = FirstLabel;
        SecondText.Text = SecondLabel;
        FirstIcon.Glyph = FirstGlyph;
        SecondIcon.Glyph = SecondGlyph;
        AutomationProperties.SetName(FirstItem, FirstLabel);
        AutomationProperties.SetName(SecondItem, SecondLabel);
        // Segmented may select its first container during initialization. Do not write that back to the rule.
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            if (generation != _generation) return;
            _ready = true;
            Selector.SelectedIndex = SecondSelected ? 1 : 0;
            Refresh(false);
        });
    }

    private void SyncSelection()
    {
        if (!_ready) return;
        Selector.SelectedIndex = SecondSelected ? 1 : 0;
        Refresh();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_ready && Selector.SelectedIndex >= 0) SecondSelected = Selector.SelectedIndex == 1;
    }

    private void Refresh(bool animate = true)
    {
        if (!_ready) return;
        for (int i = 0; i < _motions.Length; i++)
        {
            var motion = _motions[i];
            motion.Show(i == Selector.SelectedIndex, animate && _uiSettings.AnimationsEnabled);
        }
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args) =>
        ((RectangleGeometry)((Canvas)sender).Clip).Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height);

    private sealed class LabelMotion(Canvas viewport, TextBlock text, Storyboard transition,
        DoubleAnimation widthAnimation, DoubleAnimation opacityAnimation)
    {

        private double _target = -1;

        public void Reset()
        {
            transition.Stop();
            _target = -1;
        }

        public void Show(bool expanded, bool animate)
        {
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var fullWidth = Math.Ceiling(text.DesiredSize.Width) + Canvas.GetLeft(text);
            var to = expanded ? fullWidth : 0;
            if (animate && _target == to) return;
            _target = to;
            var width = viewport.Width;
            var opacity = text.Opacity;
            transition.Stop();
            viewport.Width = to;
            text.Opacity = expanded ? 1 : 0;
            if (!animate || width == to) return;

            var duration = TimeSpan.FromMilliseconds(220 + fullWidth);
            widthAnimation.From = width;
            widthAnimation.To = to;
            opacityAnimation.From = opacity;
            opacityAnimation.To = text.Opacity;
            widthAnimation.Duration = opacityAnimation.Duration = duration;
            transition.Begin();
        }
    }
}

public sealed class LogicalModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is RulesetLogicalMode.And;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value is true ? RulesetLogicalMode.And : RulesetLogicalMode.Or;
}
