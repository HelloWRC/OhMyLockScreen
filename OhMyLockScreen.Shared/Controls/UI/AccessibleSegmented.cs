using CommunityToolkit.WinUI.Controls;
using Windows.UI.Xaml.Automation.Peers;

namespace OhMyLockScreen.Shared.Controls.UI;

// CommunityToolkit.Uwp.Controls.Segmented 8.2 does not override OnCreateAutomationPeer.
// When AutomationProperties.Name is set, UWP creates a plain FrameworkElementAutomationPeer,
// causing Selector.SelectedIndex to throw InvalidCastException (E_NOINTERFACE) under UI Automation listeners.
public sealed partial class AccessibleSegmented : Segmented
{
    protected override AutomationPeer OnCreateAutomationPeer() => new AccessibleSegmentedAutomationPeer(this);
}

public sealed partial class AccessibleSegmentedAutomationPeer(Segmented owner) : ListViewBaseAutomationPeer(owner)
{
    protected override string GetClassNameCore() => nameof(Segmented);

    protected override ItemAutomationPeer OnCreateItemAutomationPeer(object item)
        => new ListViewItemDataAutomationPeer(item, this);
}
