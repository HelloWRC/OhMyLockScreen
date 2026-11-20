using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OhMyLockScreen.Models.Rules;
using OhMyLockScreen.Services.Rules;
using OhMyLockScreen.Shared.Abstractions.Services;
using OhMyLockScreen.Shared.Extensions.Registry;
using OhMyLockScreen.Shared.Helpers;
using OhMyLockScreen.Shared.Models.Media;
using OhMyLockScreen.Shared.Models.Ruleset;
using OhMyLockScreen.Shared.Services;
using Windows.UI.Core;

internal static class RulesetTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run(Action<string, Action> run)
    {
        var calls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRulesetService, RulesetService>();
        services.AddRule("true", onHandle: _ => true);
        services.AddRule("throw", onHandle: _ => throw new InvalidOperationException());
        services.AddRule("count", onHandle: _ => { calls++; return true; });
        services.AddRule<MediaFieldRuleSettings>("custom");
        using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IRulesetService>();
        evaluator.RegisterRuleHandler("custom", value => value is MediaFieldRuleSettings { Field: MediaTextField.Artist });

        run("ClassIsland empty, disabled and reversed groups", () =>
        {
            var set = new Ruleset();
            Check(!evaluator.IsRulesetSatisfied(set) && set.Groups[0].State == 0, "Default empty rule should be unknown");
            set.Mode = RulesetLogicalMode.And;
            Check(evaluator.IsRulesetSatisfied(set), "Empty group must be skipped");
            set.Groups[0].IsEnabled = false;
            Check(evaluator.IsRulesetSatisfied(set), "Disabled group must be skipped");
            set.IsReversed = true;
            Check(!evaluator.IsRulesetSatisfied(set), "Root reversal");
            set.Groups.Clear();
            Check(!evaluator.IsRulesetSatisfied(set), "No groups: false even when reversed");
        });
        run("Short circuit resets unvisited states; handler errors become false before reversal", () =>
        {
            var group = new RuleGroup { Rules = [new() { Id = "throw" }, new() { Id = "count", State = 2 }] };
            var set = new Ruleset { Groups = [group] };
            Check(!evaluator.IsRulesetSatisfied(set) && calls == 0 && group.Rules[1].State == 0, "Short circuit");
            group.Rules[0].IsReversed = true;
            group.IsReversed = true;
            Check(!evaluator.IsRulesetSatisfied(set) && calls == 1 && group.Rules.All(r => r.State == 2), "Inversions");
        });
        run("Plugin settings and missing IDs survive round trip, runtime updates do not dirty scene", () =>
        {
            var settings = new MediaFieldRuleSettings { Field = MediaTextField.Artist };
            var rule = new Rule { Id = "custom", Settings = settings };
            var set = new Ruleset { Groups = [new() { Rules = [rule] }] };
            var dirty = 0;
            using var graph = new ObservableSettingsGraph(set, (_, _, _) => dirty++);
            Check(evaluator.IsRulesetSatisfied(set) && dirty == 0, "Runtime state dirtied graph");
            settings.Text = "artist";
            Check(dirty == 1, "Nested plugin settings not observed");
            var json = JsonSerializer.Serialize(set);
            Check(!json.Contains("\"State\""), "Runtime state persisted");
            var copy = JsonSerializer.Deserialize<Ruleset>(json)!;
            Check(evaluator.IsRulesetSatisfied(copy), "JSON settings not materialized for handler");
            copy.Groups[0].Rules[0].Id = "missing";
            var before = JsonSerializer.Serialize(copy);
            Check(!evaluator.IsRulesetSatisfied(copy) && before == JsonSerializer.Serialize(copy), "Missing plugin data changed");
        });
        run("Weather code sets and signed rain thresholds match ClassIsland", () =>
        {
            var weather = new CurrentWeatherRuleSettings { WeatherId = 21 };
            Check(!BuiltinRules.MatchesWeather(weather, "22") && BuiltinRules.MatchesWeather(weather, "21"), "Exact weather");
            weather.IsFuzzyMatch = true;
            Check(BuiltinRules.MatchesWeather(weather, "22") && !BuiltinRules.MatchesWeather(weather, "0"), "Combination codes");
            foreach (var ending in new[] { false, true })
            {
                var threshold = new RainTimeRuleSettings { RainTimeMinutes = 5, IsRemainingTime = ending };
                var sign = ending ? -1 : 1;
                Check(BuiltinRules.MatchesRain(threshold, 5 * sign) && !BuiltinRules.MatchesRain(threshold, -sign) &&
                    !BuiltinRules.MatchesRain(threshold, 0) && !BuiltinRules.MatchesRain(threshold, 6 * sign), "Rain boundary");
            }
        });
        run("All media fields use the media text format; regex, session and clock boundaries", () =>
        {
            var now = DateTimeOffset.UtcNow;
            var state = new MediaState { HasSession = true, IsPlaying = true, Title = "Track", Artist = "Artist", Position = TimeSpan.FromSeconds(4),
                LastUpdatedTime = now, EndTime = TimeSpan.FromMinutes(2), TrackNumber = 5 };
            foreach (var field in Enum.GetValues<MediaTextField>())
            {
                var settings = new MediaFieldRuleSettings { Field = field, Text = state.GetText(field, now) };
                Check(settings.Matches(state, now) && !settings.Matches(MediaState.Empty, now), field.ToString());
            }
            var match = new MediaFieldRuleSettings { Field = MediaTextField.Position, Text = "0:05" };
            Check(!match.Matches(state, now) && match.Matches(state, now.AddSeconds(1)), "Clock update");
            match.Field = MediaTextField.Title;
            match.Text = "track";
            Check(!match.Matches(state, now), "Exact match must be case sensitive");
            match.UseRegex = true;
            match.Text = "^Tr";
            Check(match.Matches(state, now), "Regex match");
            match.Text = "[";
            Check(!match.Matches(state, now), "Invalid regex");
        });
        run("Unloaded observations discard queued evaluations", () =>
        {
            var dispatcher = new CoreDispatcher();
            var updates = 0;
            using var observation = new RulesetObservation(evaluator, new Ruleset(), dispatcher, _ => updates++);
            evaluator.NotifyStatusChanged();
            evaluator.NotifyStatusChanged();
            Check(updates == 1 && dispatcher.PendingCallbacks == 1, "Status notifications not marshalled and coalesced");
            observation.Dispose();
            dispatcher.Drain();
            Check(updates == 1, "Stale callback after disposal");
        });
    }
}
