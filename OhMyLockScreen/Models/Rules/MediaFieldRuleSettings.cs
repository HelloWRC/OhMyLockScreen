using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using OhMyLockScreen.Shared.Models.Media;

namespace OhMyLockScreen.Models.Rules;

public partial class MediaFieldRuleSettings : ObservableObject
{
    [ObservableProperty] private MediaTextField _field;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _useRegex;

    public bool Matches(MediaState state, DateTimeOffset now)
    {
        if (!state.HasSession) return false;
        var value = state.GetText(Field, now);
        if (!UseRegex) return value == Text;
        try
        {
            return Regex.IsMatch(value, Text, RegexOptions.None, TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException) { return false; }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
