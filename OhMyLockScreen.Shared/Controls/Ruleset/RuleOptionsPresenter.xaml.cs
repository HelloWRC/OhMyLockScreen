using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Services;
using OhMyLockScreen.Shared.Models.Ruleset;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.Ruleset;

public sealed partial class RuleOptionsPresenter : UserControl
{
    private Rule? _rule;
    private bool _updating;
    private bool _loaded;

    public RuleOptionsPresenter()
    {
        InitializeComponent();
        Loaded += (_, _) => { _loaded = true; BindRule(DataContext as Rule); };
        Unloaded += (_, _) => { _loaded = false; BindRule(null); };
        DataContextChanged += (_, _) => { if (_loaded) BindRule(DataContext as Rule); };
    }

    private void BindRule(Rule? rule)
    {
        if (_rule != null) _rule.PropertyChanged -= OnRuleChanged;
        _rule = rule;
        if (rule != null) rule.PropertyChanged += OnRuleChanged;
        Refresh();
    }

    private void OnRuleChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_updating && args.PropertyName is nameof(Rule.Id) or nameof(Rule.Settings)) Refresh();
    }

    private void Refresh()
    {
        _updating = true;
        try
        {
            SettingsPresenter.Content = null;
            if (_rule == null) { TypePicker.ItemsSource = null; return; }
            var registry = IAppHost.GetRequiredService<IRulesetService>().Rules;
            var entries = registry.Values.ToList();
            registry.TryGetValue(_rule.Id, out var info);
            if (info == null && _rule.Id.Length != 0)
            {
                info = new(_rule.Id, _rule.Id, "\uE9CE", null, null);
                entries.Add(info);
            }
            TypePicker.ItemsSource = entries;
            TypePicker.SelectedItem = info;
            if (info?.SettingsControlType is not { } controlType) return;
            var control = (RuleSettingsControlBase)IAppHost.Host!.Services.GetRequiredService(controlType);
            var settings = info.ResolveSettings(_rule.Settings);
            control.SetSettings(settings);
            _rule.Settings = settings;
            SettingsPresenter.Content = control;
        }
        finally { _updating = false; }
    }

    private void SelectType(object sender, SelectionChangedEventArgs args)
    {
        if (_updating || _rule == null || TypePicker.SelectedItem is not RuleRegistryInfo info || info.Id == _rule.Id) return;
        _updating = true;
        try
        {
            _rule.Settings = null;
            _rule.Id = info.Id;
        }
        finally { _updating = false; }
        Refresh();
    }
}
