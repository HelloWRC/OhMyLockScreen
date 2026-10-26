using System.Runtime.CompilerServices;
using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OhMyLockScreen.Shared.Controls.UI;

/// <summary>为数字输入保留空间，同时沿用 NumberBox 的原生模板与编辑行为。</summary>
public static class NumberBoxBehavior
{
    public static readonly DependencyProperty HideClearButtonProperty = DependencyProperty.RegisterAttached(
        "HideClearButton", typeof(bool), typeof(NumberBoxBehavior), new PropertyMetadata(false, OnHideClearButtonChanged));

    private static readonly ConditionalWeakTable<NumberBox, ClearButtonAppearance> HiddenButtons = new();

    public static bool GetHideClearButton(DependencyObject target) => (bool)target.GetValue(HideClearButtonProperty);

    public static void SetHideClearButton(DependencyObject target, bool value) => target.SetValue(HideClearButtonProperty, value);

    private static void OnHideClearButtonChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not NumberBox numberBox)
        {
            return;
        }

        numberBox.Loaded -= OnLoadedOrGotFocus;
        numberBox.GotFocus -= OnLoadedOrGotFocus;
        RestoreClearButton(numberBox);
        if (GetHideClearButton(numberBox))
        {
            numberBox.Loaded += OnLoadedOrGotFocus;
            numberBox.GotFocus += OnLoadedOrGotFocus;
            if (numberBox.IsLoaded)
            {
                HideClearButton(numberBox);
            }
        }
    }

    private static void OnLoadedOrGotFocus(object sender, RoutedEventArgs args) => HideClearButton((NumberBox)sender);

    private static void HideClearButton(NumberBox numberBox)
    {
        var input = numberBox.FindDescendant<TextBox>(element => element.Name == "InputBox");
        input?.ApplyTemplate();
        if (input?.FindDescendant<Button>(element => element.Name == "DeleteButton") is not { } button)
        {
            return;
        }

        if (HiddenButtons.TryGetValue(numberBox, out var previous) && ReferenceEquals(previous.Button, button))
        {
            return;
        }

        RestoreClearButton(numberBox);
        HiddenButtons.Add(numberBox, new ClearButtonAppearance(
            button, button.MinWidth, button.MaxWidth, button.Opacity, button.IsHitTestVisible));

        // The native focus states can make DeleteButton visible again. Zero its
        // layout width as well as its opacity so those states cannot take space.
        button.MinWidth = 0;
        button.MaxWidth = 0;
        button.Opacity = 0;
        button.IsHitTestVisible = false;
    }

    private static void RestoreClearButton(NumberBox numberBox)
    {
        if (HiddenButtons.TryGetValue(numberBox, out var previous))
        {
            previous.Button.MinWidth = previous.MinWidth;
            previous.Button.MaxWidth = previous.MaxWidth;
            previous.Button.Opacity = previous.Opacity;
            previous.Button.IsHitTestVisible = previous.IsHitTestVisible;
            HiddenButtons.Remove(numberBox);
        }
    }

    private sealed record ClearButtonAppearance(
        Button Button, double MinWidth, double MaxWidth, double Opacity, bool IsHitTestVisible);
}
