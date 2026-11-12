using System.Runtime.CompilerServices;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Helpers.UI;

/// <summary>为采用设置页样式的面板应用宽窗口边距。</summary>
public static class SettingsStackPanelLayout
{
    public static readonly DependencyProperty UseAdaptiveMarginProperty = DependencyProperty.RegisterAttached(
        "UseAdaptiveMargin", typeof(bool), typeof(SettingsStackPanelLayout),
        new PropertyMetadata(false, OnUseAdaptiveMarginChanged));

    private static readonly ConditionalWeakTable<StackPanel, WindowMargin> ActiveMargins = new();

    public static bool GetUseAdaptiveMargin(DependencyObject target) =>
        (bool)target.GetValue(UseAdaptiveMarginProperty);

    public static void SetUseAdaptiveMargin(DependencyObject target, bool value) =>
        target.SetValue(UseAdaptiveMarginProperty, value);

    private static void OnUseAdaptiveMarginChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not StackPanel panel)
        {
            return;
        }

        if (GetUseAdaptiveMargin(panel))
        {
            panel.Loaded += OnLoaded;
            panel.Unloaded += OnUnloaded;
            if (panel.IsLoaded)
            {
                Attach(panel);
            }
        }
        else
        {
            panel.Loaded -= OnLoaded;
            panel.Unloaded -= OnUnloaded;
            Detach(panel);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs args) => Attach((StackPanel)sender);

    private static void OnUnloaded(object sender, RoutedEventArgs args) => Detach((StackPanel)sender);

    private static void Attach(StackPanel panel) => ActiveMargins.GetValue(panel, target => new WindowMargin(target));

    private static void Detach(StackPanel panel)
    {
        if (ActiveMargins.TryGetValue(panel, out var margin))
        {
            ActiveMargins.Remove(panel);
            margin.Dispose();
        }
    }

    private sealed class WindowMargin : IDisposable
    {
        private readonly StackPanel _panel;
        private readonly Window _window;
        private readonly double _expandedThreshold;
        private readonly Thickness _expandedMargin;
        private bool _hasExpandedMargin;

        public WindowMargin(StackPanel panel)
        {
            _panel = panel;
            _window = Window.Current;
            var resources = Application.Current.Resources;
            _expandedThreshold = (double)resources["SettingsExpandedModeThresholdWidth"];
            _expandedMargin = (Thickness)resources["SettingsExpandedContentMargin"];

            UpdateMargin(_window.Bounds.Width);
            _window.SizeChanged += OnWindowSizeChanged;
            _window.Closed += OnWindowClosed;
        }

        private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs args) => UpdateMargin(args.Size.Width);

        private void OnWindowClosed(object sender, CoreWindowEventArgs args) => Detach(_panel);

        private void UpdateMargin(double width)
        {
            if (width < _expandedThreshold)
            {
                ClearExpandedMargin();
            }
            else if (_panel.ReadLocalValue(FrameworkElement.MarginProperty) == DependencyProperty.UnsetValue)
            {
                _panel.Margin = _expandedMargin;
                _hasExpandedMargin = true;
            }
        }

        private void ClearExpandedMargin()
        {
            if (_hasExpandedMargin && Equals(_panel.ReadLocalValue(FrameworkElement.MarginProperty), _expandedMargin))
            {
                _panel.ClearValue(FrameworkElement.MarginProperty);
            }

            _hasExpandedMargin = false;
        }

        public void Dispose()
        {
            _window.SizeChanged -= OnWindowSizeChanged;
            _window.Closed -= OnWindowClosed;
            ClearExpandedMargin();
        }
    }
}
