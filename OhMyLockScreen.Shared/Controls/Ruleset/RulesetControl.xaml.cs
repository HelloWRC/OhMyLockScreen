using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Abstractions.Services;
using OhMyLockScreen.Shared.Models.Ruleset;
using OhMyLockScreen.Shared.Services;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using RuleSetModel = OhMyLockScreen.Shared.Models.Ruleset.Ruleset;

namespace OhMyLockScreen.Shared.Controls.Ruleset;

public sealed partial class RulesetControl : UserControl
{
    public static readonly DependencyProperty RulesetProperty = DependencyProperty.Register(nameof(Ruleset), typeof(RuleSetModel),
        typeof(RulesetControl), new PropertyMetadata(null, (s, _) => ((RulesetControl)s).Refresh()));
    public static readonly DependencyProperty ShowTitleProperty = DependencyProperty.Register(nameof(ShowTitle), typeof(bool),
        typeof(RulesetControl), new PropertyMetadata(false, (s, _) => ((RulesetControl)s).UpdateHeading()));
    private RulesetObservation? _observation;
    private bool _loaded;

    public RuleSetModel? Ruleset
    {
        get => (RuleSetModel?)GetValue(RulesetProperty);
        set => SetValue(RulesetProperty, value);
    }
    public bool ShowTitle
    {
        get => (bool)GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }

    public RulesetControl()
    {
        InitializeComponent();
        UpdateHeading();
        Loaded += (_, _) => { _loaded = true; Refresh(); };
        Unloaded += (_, _) => { _loaded = false; _observation?.Dispose(); _observation = null; };
    }

    private void UpdateHeading()
    {
        Heading.Visibility = ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        LayoutRoot.Margin = new Thickness(ShowTitle ? 16 : 0);
    }

    private void Refresh()
    {
        _observation?.Dispose();
        LayoutRoot.DataContext = Ruleset;
        if (_loaded && Ruleset is { } model)
            _observation = new(IAppHost.GetRequiredService<IRulesetService>(), model, Dispatcher);
    }

    private void AddGroup(object sender, RoutedEventArgs args) => Ruleset?.Groups.Add(new());
    private void AddRule(object sender, RoutedEventArgs args) => ((RuleGroup)((Button)sender).DataContext).Rules.Add(new());
    private void DeleteGroup(object sender, RoutedEventArgs args) => Ruleset?.Groups.Remove((RuleGroup)((Button)sender).DataContext);
    private void CopyGroup(object sender, RoutedEventArgs args)
    {
        var group = (RuleGroup)((Button)sender).DataContext;
        Ruleset?.Groups.Add(ConfigureFileHelper.CopyObject(group));
    }
    private void DeleteRule(object sender, RoutedEventArgs args)
    {
        var rule = (Rule)((Button)sender).DataContext;
        Ruleset?.Groups.FirstOrDefault(g => g.Rules.Contains(rule))?.Rules.Remove(rule);
    }
}

public sealed class RuleStateConverter : IValueConverter
{
    public bool Label { get; set; }
    public object Convert(object value, Type targetType, object parameter, string language) => Label
        ? value switch { 1 => "不满足", 2 => "已满足", _ => "未知" }
        : new SolidColorBrush(value switch { 1 => Colors.IndianRed, 2 => Colors.Green, _ => Colors.DarkGray });
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
